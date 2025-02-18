using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CUE4Parse.UE4.AssetRegistry.Objects;
using CUE4Parse.UE4.AssetRegistry.Readers;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse.Utils;
using Newtonsoft.Json;
using Saturn.Backend.Data;
using Serilog;

namespace CUE4Parse.UE4.AssetRegistry
{
    [JsonConverter(typeof(FAssetRegistryStateConverter))]
    public class FAssetRegistryState
    {
        public FAssetData[] PreallocatedAssetDataBuffers;
        public FDependsNode[] PreallocatedDependsNodeDataBuffers;
        public FAssetPackageData[] PreallocatedPackageDataBuffers;

        public FAssetRegistryHeader Header;
        public FAssetRegistryReader Reader;

        public FAssetRegistryState(FArchive Ar)
        {
            Header = new FAssetRegistryHeader(Ar);
            var version = Header.Version;
            switch (version)
            {
                case < FAssetRegistryVersionType.AddAssetRegistryState:
                    Log.Warning("Cannot read registry state before {Version}", version);
                    break;
                case < FAssetRegistryVersionType.FixedTags:
                {
                    var nameTableReader = new FNameTableArchiveReader(Ar, Header);
                    Load(nameTableReader);
                    break;
                }
                default:
                {
                    Reader = new FAssetRegistryReader(Ar, Header);
                    Load(Reader);
                    break;
                }
            }
        }

        public (int packageSearch, int assetSearch, int packageReplace, int assetReplace) Swap(string searchPath, string replacePath)
        {
            int searchPackageIdx = -1;
            int searchAssetIdx = -1;        
            int replacePackageIdx = -1;
            int replaceAssetIdx = -1;

            for (int i = 0; i < Reader.NameMap.Length; i++)
            {
                if (String.Equals(Reader.NameMap[i].Name, searchPath, StringComparison.CurrentCultureIgnoreCase))
                {
                    searchPackageIdx = i - 1;
                    searchAssetIdx = i;
                }

                if (String.Equals(Reader.NameMap[i].Name, replacePath, StringComparison.CurrentCultureIgnoreCase))
                {
                    replacePackageIdx = i - 1;
                    replaceAssetIdx = i;
                }
                
                if (searchPackageIdx is not -1 && searchAssetIdx is not -1 && replacePackageIdx is not -1 && replaceAssetIdx is not -1) break;
            }
            
            Logger.Log("Search Package: " + searchPackageIdx);
            Logger.Log("Search Asset: " + searchAssetIdx);
            Logger.Log("Replace Package: " + replacePackageIdx);
            Logger.Log("Replace Asset: " + replaceAssetIdx);
            
            return (searchPackageIdx, searchAssetIdx, replacePackageIdx, replaceAssetIdx);
        }

        private void Load(FAssetRegistryArchive Ar)
        {
            PreallocatedAssetDataBuffers = Ar.ReadArray(() => new FAssetData(Ar));

            if (Ar.Header.Version < FAssetRegistryVersionType.RemovedMD5Hash)
                return; // Just ignore the rest of this for now.
            
            if (Ar.Header.Version < FAssetRegistryVersionType.AddedDependencyFlags)
            {
                var localNumDependsNodes = Ar.Read<int>();
                PreallocatedDependsNodeDataBuffers = new FDependsNode[localNumDependsNodes];
                for (var i = 0; i < localNumDependsNodes; i++)
                {
                    PreallocatedDependsNodeDataBuffers[i] = new FDependsNode(i);
                }
                if (localNumDependsNodes > 0)
                {
                    LoadDependencies_BeforeFlags(Ar);
                }
            }
            else
            {
                var dependencySectionSize = Ar.Read<long>();
                var dependencySectionEnd = Ar.Position + dependencySectionSize;
                var localNumDependsNodes = Ar.Read<int>();
                PreallocatedDependsNodeDataBuffers = new FDependsNode[localNumDependsNodes];
                for (var i = 0; i < localNumDependsNodes; i++)
                {
                    PreallocatedDependsNodeDataBuffers[i] = new FDependsNode(i);
                }
                if (localNumDependsNodes > 0)
                {
                    LoadDependencies(Ar);
                }
                Ar.Position = dependencySectionEnd;
            }

            PreallocatedPackageDataBuffers = Ar.ReadArray(() => new FAssetPackageData(Ar));
        }

        private void LoadDependencies_BeforeFlags(FAssetRegistryArchive Ar)
        {
            foreach (var dependsNode in PreallocatedDependsNodeDataBuffers)
            {
                dependsNode.SerializeLoad_BeforeFlags(Ar, PreallocatedDependsNodeDataBuffers);
            }
        }

        private void LoadDependencies(FAssetRegistryArchive Ar)
        {
            foreach (var dependsNode in PreallocatedDependsNodeDataBuffers)
            {
                dependsNode.SerializeLoad(Ar, PreallocatedDependsNodeDataBuffers);
            }
        }
    }

    public class FAssetRegistryStateConverter : JsonConverter<FAssetRegistryState>
    {
        public override void WriteJson(JsonWriter writer, FAssetRegistryState value, JsonSerializer serializer)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("PreallocatedAssetDataBuffers");
            serializer.Serialize(writer, value.PreallocatedAssetDataBuffers);

            writer.WritePropertyName("PreallocatedDependsNodeDataBuffers");
            serializer.Serialize(writer, value.PreallocatedDependsNodeDataBuffers);

            writer.WritePropertyName("PreallocatedPackageDataBuffers");
            serializer.Serialize(writer, value.PreallocatedPackageDataBuffers);

            writer.WriteEndObject();
        }

        public override FAssetRegistryState ReadJson(JsonReader reader, Type objectType, FAssetRegistryState existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}