using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Unversioned;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Assets.Utils;
using CUE4Parse.UE4.Exceptions;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Utils;
using Saturn.Backend.Data;
using Serilog;

namespace CUE4Parse.UE4.Assets
{
    [StructLayout(LayoutKind.Sequential)]
    public struct FInternalArc
    {
        public int FromExportBundleIndex;
        public int ToExportBundleIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FExternalArc
    {
        public int FromImportIndex;
        public EExportCommandType FromCommandType;
        public int ToExportBundleIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FDependencyBundleHeader
    {
        public int FirstEntryIndex;
        public uint[,] EntryCount = new uint[(int)EExportCommandType.ExportCommandType_Count, (int)EExportCommandType.ExportCommandType_Count];

        public FDependencyBundleHeader(FAssetArchive Ar)
        {
            FirstEntryIndex = Ar.Read<int>();
            for (int i = 0; i < (int)EExportCommandType.ExportCommandType_Count; i++)
            {
                for (int j = 0; j < (int)EExportCommandType.ExportCommandType_Count; j++)
                {
                    EntryCount[i, j] = Ar.Read<uint>();
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FDependencyBundleEntry
    {
        public FPackageIndex LocalImportOrExportIndex;

        public FDependencyBundleEntry(FAssetArchive Ar)
        {
            LocalImportOrExportIndex = new FPackageIndex(Ar);
        }
    }

    [SkipObjectRegistration]
    public sealed class IoPackage : AbstractUePackage
    {
        public static List<string> NameToBeSearching = new();
        public readonly IoGlobalData GlobalData;

        public override FPackageFileSummary Summary { get; }
        public override List<FNameEntrySerialized> NameMap { get; }
        private readonly ulong[]? ImportedPublicExportHashes;
        public readonly FPackageObjectIndex[] ImportMap;
        private readonly FExportMapEntry[] ExportMap;
        public readonly FBulkDataMapEntry[] BulkDataMap;

        private readonly Lazy<IoPackage?[]> ImportedPackages;
        public override Lazy<UObject>[] ExportsLazy { get; }
        private List<UObject> Exports { get; set; } = new();
        private bool HasLoadedExports = false;
        public override bool IsFullyLoaded { get; }

        private List<FNameEntrySerialized> nameMap = new();
        public FZenPackageSummary summary { get; set; }
        private FExportBundleHeader[]? exportBundleHeaders;
        private FExportBundleEntry[] exportBundleEntries;
        
        public ulong bulkDataMapSize;

        public Dictionary<int, int> ExtraExportSize = new();
        public Dictionary<string, string> PathOverrides = new();
        
        // Graph Data <= UE 5.2
        private List<FInternalArc> InternalArcs = new();
        private List<List<FExternalArc>> ExternalArcs = new();
        
        // Graph Data > UE 5.2
        private List<FDependencyBundleHeader> DependencyBundleHeaders = new();
        private List<FDependencyBundleEntry> DependencyBundleEntries = new();
        

        public long TotalSize { get; }

        public IoPackage(
            FArchive uasset, IoGlobalData globalData, FIoContainerHeader? containerHeader = null,
            Lazy<FArchive?>? ubulk = null, Lazy<FArchive?>? uptnl = null,
            IFileProvider? provider = null, TypeMappings? mappings = null) : base(uasset.Name.SubstringBeforeLast('.'), provider, mappings)
        {
            GlobalData = globalData;
            var uassetAr = new FAssetArchive(uasset, this);
            TotalSize = uassetAr.Length;
            
            FPackageId[] importedPackageIds;
            int cookedHeaderSize;
            int allExportDataOffset;

            if (uassetAr.Game >= EGame.GAME_UE5_0)
            {
                // Summary
                summary = new FZenPackageSummary(uassetAr);
                Summary = new FPackageFileSummary
                {
                    PackageFlags = summary.PackageFlags,
                    TotalHeaderSize = summary.GraphDataOffset + (int) summary.HeaderSize,
                    ExportCount = (summary.ExportBundleEntriesOffset - summary.ExportMapOffset) / FExportMapEntry.Size,
                    ImportCount = (summary.ExportMapOffset - summary.ImportMapOffset) / FPackageObjectIndex.Size
                };

                // Versioning info
                if (summary.bHasVersioningInfo != 0)
                {
                    var versioningInfo = new FZenPackageVersioningInfo(uassetAr);
                    Summary.FileVersionUE = versioningInfo.PackageVersion;
                    Summary.FileVersionLicenseeUE = (EUnrealEngineObjectLicenseeUEVersion) versioningInfo.LicenseeVersion;
                    Summary.CustomVersionContainer = versioningInfo.CustomVersions;
                    if (!uassetAr.Versions.bExplicitVer)
                    {
                        uassetAr.Versions.Ver = versioningInfo.PackageVersion;
                        uassetAr.Versions.CustomVersions = versioningInfo.CustomVersions;
                    }
                }
                else
                {
                    Summary.bUnversioned = true;
                }

                // Name map
                NameMap = FNameEntrySerialized.LoadNameBatch(uassetAr).ToList();
                nameMap = NameMap.ToList();
                Name = CreateFNameFromMappedName(summary.Name).Text;

                // Find store entry by package name
                FFilePackageStoreEntry? storeEntry = null;
                if (containerHeader != null)
                {
                    var packageId = FPackageId.FromName(Name);
                    var storeEntryIdx = Array.IndexOf(containerHeader.PackageIds, FPackageId.FromName(Name));
                    if (storeEntryIdx != -1)
                    {
                        storeEntry = containerHeader.StoreEntries[storeEntryIdx];
                    }
                    else
                    {
                        var optionalSegmentStoreEntryIdx = Array.IndexOf(containerHeader.OptionalSegmentPackageIds, packageId);
                        if (optionalSegmentStoreEntryIdx != -1)
                        {
                            storeEntry = containerHeader.OptionalSegmentStoreEntries[optionalSegmentStoreEntryIdx];
                        }
                        else
                        {
                            Log.Warning("Couldn't find store entry for package {0}, its data will not be fully read", Name);
                        }
                    }
                }

                BulkDataMap = Array.Empty<FBulkDataMapEntry>();
                if (uassetAr.Ver >= EUnrealEngineObjectUE5Version.DATA_RESOURCES)
                {
                    var bulkDataMapSize = uassetAr.Read<ulong>();
                    BulkDataMap = uassetAr.ReadArray<FBulkDataMapEntry>((int) (bulkDataMapSize / FBulkDataMapEntry.Size));
                }

                // Imported public export hashes
                uassetAr.Position = summary.ImportedPublicExportHashesOffset;
                ImportedPublicExportHashes = uassetAr.ReadArray<ulong>((summary.ImportMapOffset - summary.ImportedPublicExportHashesOffset) / sizeof(ulong));

                // Import map
                uassetAr.Position = summary.ImportMapOffset;
                ImportMap = uasset.ReadArray<FPackageObjectIndex>(Summary.ImportCount);

                // Export map
                uassetAr.Position = summary.ExportMapOffset;
                ExportMap = uasset.ReadArray(Summary.ExportCount, () => new FExportMapEntry(uassetAr));
                ExportsLazy = new Lazy<UObject>[Summary.ExportCount];

                // Export bundle entries
                uassetAr.Position = summary.ExportBundleEntriesOffset;
                exportBundleEntries = uassetAr.ReadArray<FExportBundleEntry>(Summary.ExportCount * 2);

                if (uassetAr.Game < EGame.GAME_UE5_2)
                {
                    // Export bundle headers
                    uassetAr.Position = summary.GraphDataOffset;
                    var exportBundleHeadersCount = storeEntry?.ExportBundleCount ?? 1;
                    exportBundleHeaders = uassetAr.ReadArray<FExportBundleHeader>(exportBundleHeadersCount);
                    
                    // Graph Data
                    int InternalArcsCount = uassetAr.Read<int>();

                    for (int i = 0; i < InternalArcsCount; ++i)
                    {
                        InternalArcs.Add(uassetAr.Read<FInternalArc>());
                    }

                    foreach (FPackageId ImportedPackageId in storeEntry?.ImportedPackages ?? Array.Empty<FPackageId>())
                    {
                        int ExternalArcsCount = uassetAr.Read<int>();
                        List<FExternalArc> ExternalArcsForPackage = new List<FExternalArc>();
                    
                        for (int i = 0; i < ExternalArcsCount; ++i)
                        {
                            FExternalArc externalArc = new FExternalArc
                            {
                                FromImportIndex = uassetAr.Read<int>(),
                                FromCommandType = (EExportCommandType)uassetAr.Read<byte>(),
                                ToExportBundleIndex = uassetAr.Read<int>()
                            };
                            ExternalArcsForPackage.Add(externalArc);
                        }
                        ExternalArcs.Add(ExternalArcsForPackage);
                    }
                }
                else
                {
                    exportBundleHeaders = null;
                    
                    // Graph Data
                    foreach (var _ in exportBundleEntries)
                    {
                        DependencyBundleHeaders.Add(new FDependencyBundleHeader(uassetAr));
                    }
                    
                    foreach (var _ in exportBundleEntries)
                    {
                        DependencyBundleEntries.Add(new FDependencyBundleEntry(uassetAr));
                    }
                }

                importedPackageIds = storeEntry?.ImportedPackages ?? Array.Empty<FPackageId>();
                cookedHeaderSize = (int) summary.CookedHeaderSize;

                allExportDataOffset = (int) summary.HeaderSize;
            }
            else
            {
                return;
            }

            // Preload dependencies
            ImportedPackages = new Lazy<IoPackage?[]>(provider != null ? () =>
            {
                var packages = new IoPackage?[importedPackageIds.Length];
                for (var i = 0; i < importedPackageIds.Length; i++)
                {
                    provider.TryLoadPackage(importedPackageIds[i], out packages[i]);
                }
                return packages;
            } : Array.Empty<IoPackage?>);

            // Attach ubulk and uptnl
            if (ubulk != null) uassetAr.AddPayload(PayloadType.UBULK, Summary.BulkDataStartOffset, ubulk);
            if (uptnl != null) uassetAr.AddPayload(PayloadType.UPTNL, Summary.BulkDataStartOffset, uptnl);

            if (HasFlags(EPackageFlags.PKG_UnversionedProperties) && mappings == null)
                throw new ParserException("Package has unversioned properties but mapping file is missing, can't serialize");
            
            // Populate lazy exports
            int ProcessEntry(FExportBundleEntry entry, int pos, bool newPos)
            {
                if (entry.CommandType != EExportCommandType.ExportCommandType_Serialize)
                    return 0; // Skip ExportCommandType_Create

                var export = ExportMap[entry.LocalExportIndex];
                ExportsLazy[entry.LocalExportIndex] = new Lazy<UObject>(() =>
                {
                    // Create
                    var obj = ConstructObject(ResolveObjectIndex(export.ClassIndex)?.Object?.Value as UStruct);
                    obj.Name = CreateFNameFromMappedName(export.ObjectName).Text;
                    obj.Outer = (ResolveObjectIndex(export.OuterIndex) as ResolvedExportObject)?.ExportObject.Value ?? this;
                    obj.Super = ResolveObjectIndex(export.SuperIndex) as ResolvedExportObject;
                    obj.Template = ResolveObjectIndex(export.TemplateIndex) as ResolvedExportObject;
                    obj.Flags |= export.ObjectFlags; // We give loaded objects the RF_WasLoaded flag in ConstructObject, so don't remove it again in here

                    // Serialize
                    var Ar = (FAssetArchive) uassetAr.Clone();
                    Ar.AbsoluteOffset = newPos ? cookedHeaderSize - allExportDataOffset : (int) export.CookedSerialOffset - pos;
                    Ar.Position = pos;
                    DeserializeObject(obj, Ar, (long) export.CookedSerialSize);
                    // TODO right place ???
                    obj.Flags |= EObjectFlags.RF_LoadCompleted;
                    obj.PostLoad();
                    return obj;
                });
                return (int) export.CookedSerialSize;
            }
            
            if (exportBundleHeaders != null) // 4.26 - 5.2
            {
                foreach (var exportBundle in exportBundleHeaders)
                {
                    var currentExportDataOffset = allExportDataOffset;
                    for (var i = 0u; i < exportBundle.EntryCount; i++)
                    {
                        currentExportDataOffset += ProcessEntry(exportBundleEntries[exportBundle.FirstEntryIndex + i], currentExportDataOffset, false);
                    }
                    Summary.BulkDataStartOffset = currentExportDataOffset;
                }
            }
            else foreach (var entry in exportBundleEntries)
            {
                ProcessEntry(entry, allExportDataOffset + (int) ExportMap[entry.LocalExportIndex].CookedSerialOffset, true);
            }

            
            IsFullyLoaded = true;
        }

        public IoPackage(FArchive uasset, IoGlobalData globalData, FIoContainerHeader? containerHeader = null, FArchive? ubulk = null, FArchive? uptnl = null, IFileProvider? provider = null, TypeMappings? mappings = null)
            : this(uasset, globalData, containerHeader, ubulk != null ? new Lazy<FArchive?>(() => ubulk) : null, uptnl != null ? new Lazy<FArchive?>(() => uptnl) : null, provider, mappings) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private FName CreateFNameFromMappedName(FMappedName mappedName) =>
            new(mappedName, mappedName.IsGlobal ? GlobalData.GlobalNameMap : NameMap.ToArray());

        public void ReplaceProperty<T>(string propertyName, FPropertyTagType<T> value)
        {
            if (!HasLoadedExports)
            {
                foreach (var export in ExportsLazy)
                {
                    if (export == null)
                        throw new Exception("There is a null export in the lazy export array!");
                    Exports.Add(export.Value);
                }

                HasLoadedExports = true;
            }
            
            foreach (var property in Exports.SelectMany(t => t.Properties))
            {
                if (property.Name == propertyName)
                {
                    ((FPropertyTagType<T>)property.Tag).Value = value.Value;
                    return;
                }
                    
                switch (property.Tag.GenericValue)
                {
                    case FStructFallback structFallback:
                    {
                        foreach (var prop in structFallback.Properties)
                        {
                            if (prop.Name == propertyName)
                            {
                                ((FPropertyTagType<T>)prop.Tag).Value = value.Value;
                                return;
                            }
                        }

                        break;
                    }
                    case UScriptArray array:
                    {
                        foreach (var prop in array.Properties)
                        {
                            if (prop.GenericValue is UScriptStruct scriptStruct)
                            {
                                switch (scriptStruct.StructType)
                                {
                                    case FStructFallback fallback:
                                        foreach (var fallbackProperty in fallback.Properties)
                                        {
                                            if (fallbackProperty.Name == propertyName)
                                            {
                                                ((FPropertyTagType<T>)fallbackProperty.Tag).Value = value.Value;
                                                return;
                                            }
                                        }
                                        break;
                                    default:
                                        throw new Exception("Unknown struct type: " + scriptStruct.StructType.GetType().Name);
                                }
                            }
                        }

                        break;
                    }
                }
            }

            throw new Exception("Couldn't find property! Property Name: " + propertyName);
        }
        
        public void AddToArray<T>(string arrayName, FPropertyTagType<T> value)
        {
            if (!HasLoadedExports)
            {
                foreach (var export in ExportsLazy)
                {
                    if (export == null)
                        throw new Exception("There is a null export in the lazy export array!");
                    Exports.Add(export.Value);
                }

                HasLoadedExports = true;
            }
            
            for (int i = 0; i < Exports.Count; i++)
            {
                foreach (var property in Exports[i].Properties)
                {
                    if (property.Name == arrayName)
                    {
                        var tagData = (UScriptArray)property.Tag.GenericValue;
                        tagData.Properties.Add(tagData.Properties[0]);
                        tagData.Properties[^1] = value;

                        switch (value.Value)
                        {
                            case SoftObjectProperty softObjectProperty:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] += (softObjectProperty.Value.AssetPathName.Text.Contains('.') ? 20 : 12) + (softObjectProperty.Value.SubPathString.Length == 0 ? 0 : softObjectProperty.Value.SubPathString.Length + 1);
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), (softObjectProperty.Value.AssetPathName.Text.Contains('.') ? 20 : 12) + (softObjectProperty.Value.SubPathString.Length == 0 ? 0 : softObjectProperty.Value.SubPathString.Length + 1));
                                break;
                            case FSoftObjectPath softObjectPath:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] += (softObjectPath.AssetPathName.Text.Contains('.') ? 20 : 12) + (softObjectPath.SubPathString.Length == 0 ? 0 : softObjectPath.SubPathString.Length + 1);
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), (softObjectPath.AssetPathName.Text.Contains('.') ? 20 : 12) + (softObjectPath.SubPathString.Length == 0 ? 0 : softObjectPath.SubPathString.Length + 1));
                                break;
                            case IntProperty intProperty:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] += 4;
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), 4);
                                break;
                            case Int8Property int8Property:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] += 1;
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), 1);
                                break;
                            case Int16Property int16Property:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] += 2;
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), 2);
                                break;
                            case Int64Property int64Property:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] += 8;
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), 8);
                                break;
                            case FloatProperty floatProperty:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] += 4;
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), 4);
                                break;
                            case DoubleProperty doubleProperty:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] += 8;
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), 8);
                                break;
                            default:
                                throw new Exception("Unsupported type!\nSend this in a support ticket in Saturn's Discord!\n" + value.Value.GetType());
                        }
                    }
                }
            }
        }
        
        public void RemoveFromArray(string arrayName, int idx)
        {
            if (!HasLoadedExports)
            {
                foreach (var export in ExportsLazy)
                {
                    if (export == null)
                        throw new Exception("There is a null export in the lazy export array!");
                    Exports.Add(export.Value);
                }

                HasLoadedExports = true;
            }
            
            for (int i = 0; i < Exports.Count; i++)
            {
                foreach (var property in Exports[i].Properties)
                {
                    if (property.Name == arrayName)
                    {
                        var tagData = (UScriptArray)property.Tag.GenericValue;

                        switch (tagData.Properties[idx].GenericValue)
                        {
                            case SoftObjectProperty softObjectProperty:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] -= ((softObjectProperty.Value.AssetPathName.Text.Contains('.') ? 20 : 12) + (softObjectProperty.Value.SubPathString.Length == 0 ? 0 : softObjectProperty.Value.SubPathString.Length + 1));
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), -((softObjectProperty.Value.AssetPathName.Text.Contains('.') ? 20 : 12) + (softObjectProperty.Value.SubPathString.Length == 0 ? 0 : softObjectProperty.Value.SubPathString.Length + 1)));
                                break;
                            case FSoftObjectPath softObjectPath:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] -= ((softObjectPath.AssetPathName.Text.Contains('.') ? 20 : 12) + (softObjectPath.SubPathString.Length == 0 ? 0 : softObjectPath.SubPathString.Length + 1));
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), -((softObjectPath.AssetPathName.Text.Contains('.') ? 20 : 12) + (softObjectPath.SubPathString.Length == 0 ? 0 : softObjectPath.SubPathString.Length + 1)));
                                break;
                            case IntProperty intProperty:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] -= 4;
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), -4);
                                break;
                            case Int8Property int8Property:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] -= 1;
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), -1);
                                break;
                            case Int16Property int16Property:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] -= 2;
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), -2);
                                break;
                            case Int64Property int64Property:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] -= 8;
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), -8);
                                break;
                            case FloatProperty floatProperty:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] -= 4;
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), -4);
                                break;
                            case DoubleProperty doubleProperty:
                                if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                    ExtraExportSize[Math.Abs(i - Exports.Count)] -= 8;
                                else
                                    ExtraExportSize.Add(Math.Abs(i - Exports.Count), -8);
                                break;
                            default:
                                throw new Exception("Unsupported type!\nSend this in a support ticket in Saturn's Discord!\n" + tagData.Properties[idx].GenericValue.GetType());
                        }
                        
                        tagData.Properties.Remove(tagData.Properties[idx]);
                        return;
                    }
                    
                    switch (property.Tag.GenericValue)
                    {
                        case FStructFallback structFallback:
                        {
                            foreach (var prop in structFallback.Properties)
                            {
                                if (prop.Name == arrayName)
                                {
                                    var tagData = (UScriptArray)prop.Tag.GenericValue;

                                    switch (tagData.Properties[idx].GenericValue)
                                    {
                                        case SoftObjectProperty softObjectProperty:
                                            if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -=
                                                    (softObjectProperty.Value.AssetPathName.Text.Contains('.')
                                                        ? 20
                                                        : 12) + softObjectProperty.Value.SubPathString.Length == 0
                                                        ? 0
                                                        : softObjectProperty.Value.SubPathString.Length + 1;
                                            else
                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count),
                                                    -(softObjectProperty.Value.AssetPathName.Text.Contains('.')
                                                        ? 20
                                                        : 12) + softObjectProperty.Value.SubPathString.Length == 0
                                                        ? 0
                                                        : softObjectProperty.Value.SubPathString.Length + 1);
                                            break;
                                        case FSoftObjectPath softObjectPath:
                                            if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -=
                                                    (softObjectPath.AssetPathName.Text.Contains('.') ? 20 : 12) +
                                                    softObjectPath.SubPathString.Length == 0
                                                        ? 0
                                                        : softObjectPath.SubPathString.Length + 1;
                                            else
                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count),
                                                    -(softObjectPath.AssetPathName.Text.Contains('.') ? 20 : 12) +
                                                    softObjectPath.SubPathString.Length == 0
                                                        ? 0
                                                        : softObjectPath.SubPathString.Length + 1);
                                            break;
                                        case IntProperty intProperty:
                                            if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -= 4;
                                            else
                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count), -4);
                                            break;
                                        case Int8Property int8Property:
                                            if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -= 1;
                                            else
                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count), -1);
                                            break;
                                        case Int16Property int16Property:
                                            if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -= 2;
                                            else
                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count), -2);
                                            break;
                                        case Int64Property int64Property:
                                            if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -= 8;
                                            else
                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count), -8);
                                            break;
                                        case FloatProperty floatProperty:
                                            if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -= 4;
                                            else
                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count), -4);
                                            break;
                                        case DoubleProperty doubleProperty:
                                            if (ExtraExportSize.ContainsKey(Math.Abs(i - Exports.Count)))
                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -= 8;
                                            else
                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count), -8);
                                            break;
                                        default:
                                            throw new Exception(
                                                "Unsupported type!\nSend this in a support ticket in Saturn's Discord!\n" +
                                                tagData.Properties[idx].GenericValue.GetType());
                                    }

                                    tagData.Properties.Remove(tagData.Properties[idx]);
                                    return;
                                }
                            }

                            break;
                        }
                        case UScriptArray array:
                        {
                            foreach (var prop in array.Properties)
                            {
                                if (prop.GenericValue is UScriptStruct scriptStruct)
                                {
                                    switch (scriptStruct.StructType)
                                    {
                                        case FStructFallback fallback:
                                            foreach (var fallbackProperty in fallback.Properties)
                                            {
                                                if (fallbackProperty.Name == arrayName)
                                                {
                                                    var tagData = (UScriptArray)fallbackProperty.Tag.GenericValue;

                                                    switch (tagData.Properties[idx].GenericValue)
                                                    {
                                                        case SoftObjectProperty softObjectProperty:
                                                            if (ExtraExportSize.ContainsKey(
                                                                    Math.Abs(i - Exports.Count)))
                                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -=
                                                                    (softObjectProperty.Value.AssetPathName.Text
                                                                        .Contains('.')
                                                                        ? 20
                                                                        : 12) + softObjectProperty.Value.SubPathString
                                                                        .Length == 0
                                                                        ? 0
                                                                        : softObjectProperty.Value.SubPathString
                                                                            .Length + 1;
                                                            else
                                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count),
                                                                    -(softObjectProperty.Value.AssetPathName.Text
                                                                        .Contains('.')
                                                                        ? 20
                                                                        : 12) + softObjectProperty.Value.SubPathString
                                                                        .Length == 0
                                                                        ? 0
                                                                        : softObjectProperty.Value.SubPathString
                                                                            .Length + 1);
                                                            break;
                                                        case FSoftObjectPath softObjectPath:
                                                            if (ExtraExportSize.ContainsKey(
                                                                    Math.Abs(i - Exports.Count)))
                                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -=
                                                                    (softObjectPath.AssetPathName.Text.Contains('.')
                                                                        ? 20
                                                                        : 12) + softObjectPath.SubPathString.Length == 0
                                                                        ? 0
                                                                        : softObjectPath.SubPathString.Length + 1;
                                                            else
                                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count),
                                                                    -(softObjectPath.AssetPathName.Text.Contains('.')
                                                                        ? 20
                                                                        : 12) + softObjectPath.SubPathString.Length == 0
                                                                        ? 0
                                                                        : softObjectPath.SubPathString.Length + 1);
                                                            break;
                                                        case IntProperty intProperty:
                                                            if (ExtraExportSize.ContainsKey(
                                                                    Math.Abs(i - Exports.Count)))
                                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -= 4;
                                                            else
                                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count), -4);
                                                            break;
                                                        case Int8Property int8Property:
                                                            if (ExtraExportSize.ContainsKey(
                                                                    Math.Abs(i - Exports.Count)))
                                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -= 1;
                                                            else
                                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count), -1);
                                                            break;
                                                        case Int16Property int16Property:
                                                            if (ExtraExportSize.ContainsKey(
                                                                    Math.Abs(i - Exports.Count)))
                                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -= 2;
                                                            else
                                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count), -2);
                                                            break;
                                                        case Int64Property int64Property:
                                                            if (ExtraExportSize.ContainsKey(
                                                                    Math.Abs(i - Exports.Count)))
                                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -= 8;
                                                            else
                                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count), -8);
                                                            break;
                                                        case FloatProperty floatProperty:
                                                            if (ExtraExportSize.ContainsKey(
                                                                    Math.Abs(i - Exports.Count)))
                                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -= 4;
                                                            else
                                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count), -4);
                                                            break;
                                                        case DoubleProperty doubleProperty:
                                                            if (ExtraExportSize.ContainsKey(
                                                                    Math.Abs(i - Exports.Count)))
                                                                ExtraExportSize[Math.Abs(i - Exports.Count)] -= 8;
                                                            else
                                                                ExtraExportSize.Add(Math.Abs(i - Exports.Count), -8);
                                                            break;
                                                        default:
                                                            throw new Exception(
                                                                "Unsupported type!\nSend this in a support ticket in Saturn's Discord!\n" +
                                                                tagData.Properties[idx].GenericValue.GetType());
                                                    }

                                                    tagData.Properties.Remove(tagData.Properties[idx]);
                                                    return;
                                                }
                                            }
                                            break;
                                        default:
                                            throw new Exception("Unknown struct type: " + scriptStruct.StructType.GetType().Name);
                                    }
                                }
                            }

                            break;
                        }
                    }
                }
            }
        }

        private void LoadExportBundles(FArchive Ar, int graphDataSize, out FExportBundleHeader[] bundleHeadersArray, out FExportBundleEntry[] bundleEntriesArray)
        {
            var remainingBundleEntryCount = graphDataSize / (4 + 4);
            var foundBundlesCount = 0;
            var foundBundleHeaders = new List<FExportBundleHeader>();
            while (foundBundlesCount < remainingBundleEntryCount)
            {
                // This location is occupied by header, so it is not a bundle entry
                remainingBundleEntryCount--;
                var bundleHeader = new FExportBundleHeader(Ar);
                foundBundlesCount += (int) bundleHeader.EntryCount;
                foundBundleHeaders.Add(bundleHeader);
            }

            if (foundBundlesCount != remainingBundleEntryCount)
                throw new ParserException(Ar, $"FoundBundlesCount {foundBundlesCount} != RemainingBundleEntryCount {remainingBundleEntryCount}");

            // Load export bundles into arrays
            bundleHeadersArray = foundBundleHeaders.ToArray();
            bundleEntriesArray = Ar.ReadArray<FExportBundleEntry>(foundBundlesCount);
        }

        private FPackageId[] LoadGraphData(FArchive Ar)
        {
            var packageCount = Ar.Read<int>();
            if (packageCount == 0) return Array.Empty<FPackageId>();

            var packageIds = new FPackageId[packageCount];
            for (var packageIndex = 0; packageIndex < packageCount; packageIndex++)
            {
                var packageId = Ar.Read<FPackageId>();
                var bundleCount = Ar.Read<int>();
                Ar.Position += bundleCount * (sizeof(int) + sizeof(int)); // Skip FArcs
                packageIds[packageIndex] = packageId;
            }

            return packageIds;
        }

        public override UObject? GetExportOrNull(string name, StringComparison comparisonType = StringComparison.Ordinal)
        {
            for (var i = 0; i < ExportMap.Length; i++)
            {
                var export = ExportMap[i];
                if (CreateFNameFromMappedName(export.ObjectName).Text.Equals(name, comparisonType))
                {
                    return ExportsLazy[i].Value;
                }
            }

            return null;
        }

        public override ResolvedObject? ResolvePackageIndex(FPackageIndex? index)
        {
            if (index == null || index.IsNull)
                return null;
            if (index.IsImport && -index.Index - 1 < ImportMap.Length)
                return ResolveObjectIndex(ImportMap[-index.Index - 1]);
            if (index.IsExport && index.Index - 1 < ExportMap.Length)
                return new ResolvedExportObject(index.Index - 1, this);
            return null;
        }

        public ResolvedObject? ResolveObjectIndex(FPackageObjectIndex index)
        {
            if (index.IsNull)
            {
                return null;
            }

            if (index.IsExport)
            {
                return new ResolvedExportObject((int) index.AsExport, this);
            }

            if (index.IsScriptImport)
            {
                if (GlobalData.ScriptObjectEntriesMap.TryGetValue(index, out var scriptObjectEntry))
                {
                    return new ResolvedScriptObject(scriptObjectEntry, this);
                }
            }

            if (index.IsPackageImport && Provider != null)
            {
                if (ImportedPublicExportHashes != null)
                {
                    var packageImportRef = index.AsPackageImportRef;
                    var importedPackages = ImportedPackages.Value;
                    if (packageImportRef.ImportedPackageIndex < importedPackages.Length)
                    {
                        var pkg = importedPackages[packageImportRef.ImportedPackageIndex];
                        if (pkg != null)
                        {
                            for (int exportIndex = 0; exportIndex < pkg.ExportMap.Length; ++exportIndex)
                            {
                                if (pkg.ExportMap[exportIndex].PublicExportHash == ImportedPublicExportHashes[packageImportRef.ImportedPublicExportHashIndex])
                                {
                                    return new ResolvedExportObject(exportIndex, pkg);
                                }
                            }
                        }
                    }
                }
                else
                {
                    foreach (var pkg in ImportedPackages.Value)
                    {
                        if (pkg != null)
                        {
                            for (int exportIndex = 0; exportIndex < pkg.ExportMap.Length; ++exportIndex)
                            {
                                if (pkg.ExportMap[exportIndex].GlobalImportIndex == index)
                                {
                                    return new ResolvedExportObject(exportIndex, pkg);
                                }
                            }
                        }
                    }
                }
            }

            Log.Warning("Missing {0} import 0x{1:X} for package {2}", index.IsScriptImport ? "script" : "package", index.Value, Name);
            return null;
        }

        private class ResolvedExportObject : ResolvedObject
        {
            public FExportMapEntry ExportMapEntry;
            public Lazy<UObject> ExportObject;

            public ResolvedExportObject(int exportIndex, IoPackage package) : base(package, exportIndex)
            {
                if (exportIndex >= package.ExportMap.Length) return;
                ExportMapEntry = package.ExportMap[exportIndex];
                ExportObject = package.ExportsLazy[exportIndex];
            }

            public override FName Name => ((IoPackage) Package).CreateFNameFromMappedName(ExportMapEntry.ObjectName);
            public override ResolvedObject Outer => ((IoPackage) Package).ResolveObjectIndex(ExportMapEntry.OuterIndex) ?? new ResolvedLoadedObject((UObject) Package);
            public override ResolvedObject? Class => ((IoPackage) Package).ResolveObjectIndex(ExportMapEntry.ClassIndex);
            public override ResolvedObject? Super => ((IoPackage) Package).ResolveObjectIndex(ExportMapEntry.SuperIndex);
            public override Lazy<UObject> Object => ExportObject;
        }

        private class ResolvedScriptObject : ResolvedObject
        {
            public FScriptObjectEntry ScriptImport;

            public ResolvedScriptObject(FScriptObjectEntry scriptImport, IoPackage package) : base(package)
            {
                ScriptImport = scriptImport;
            }

            public override FName Name => ((IoPackage) Package).CreateFNameFromMappedName(ScriptImport.ObjectName);
            public override ResolvedObject? Outer => ((IoPackage) Package).ResolveObjectIndex(ScriptImport.OuterIndex);
            // This means we'll have UScriptStruct's shown as UClass which is wrong.
            // Unfortunately because the mappings format does not distinguish between classes and structs, there's no other way around :(
            public override ResolvedObject Class => new ResolvedLoadedObject(new UScriptClass("Class"));
            public override Lazy<UObject> Object => new(() => new UScriptClass(Name.Text));
        }
    }
}