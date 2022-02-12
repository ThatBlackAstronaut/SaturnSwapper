﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;
using Saturn.Backend.Data.Enums;
using Saturn.Backend.Data.Models.FortniteAPI;
using Saturn.Backend.Data.Models.Items;
using Saturn.Backend.Data.Services;
using Saturn.Backend.Data.Utils;
using Saturn.Backend.Data.Utils.Swaps;

namespace Saturn.Backend.Data.SwapOptions.Skins;

internal abstract class SkinSwap : AbstractSwap
{
    protected SkinSwap(string name, string rarity, string icon, MeshDefaultModel swapModel)
        : base(name, rarity, icon)
    {
        SwapModel = swapModel;
    }

    public MeshDefaultModel SwapModel { get; }
}

public class AddSkins
{
    public static List<SaturnItem> SkinOptions = new List<SaturnItem>()
    {
        new SaturnItem
        {
            ItemDefinition = "CID_970_Athena_Commando_F_RenegadeRaiderHoliday",
            Name = "Gingerbread Raider",
            Description = "Let the festivities begin.",
            Icon =
                "https://fortnite-api.com/images/cosmetics/br/cid_970_athena_commando_f_renegaderaiderholiday/smallicon.png",
            Rarity = "Rare"
        },
        new SaturnItem
        {
            ItemDefinition = "CID_784_Athena_Commando_F_RenegadeRaiderFire",
            Name = "Blaze",
            Description = "Fill the world with flames.",
            Icon =
                "https://fortnite-api.com/images/cosmetics/br/cid_784_athena_commando_f_renegaderaiderfire/smallicon.png",
            Rarity = "Legendary"
        },
        new SaturnItem
        {
            ItemDefinition = "CID_A_322_Athena_Commando_F_RenegadeRaiderIce",
            Name = "Permafrost Raider",
            Description = "What could freeze a heart that burns?",
            Icon =
                "https://fortnite-api.com/images/cosmetics/br/cid_a_322_athena_commando_f_renegaderaiderice/smallicon.png",
            Rarity = "Epic",
            Series = "FrozenSeries"
        },
        new SaturnItem
        {
            ItemDefinition = "CID_653_Athena_Commando_F_UglySweaterFrozen",
            Name = "Frozen Nog Ops",
            Description = "Bring some chill to the skirmish.",
            Icon =
                "https://fortnite-api.com/images/cosmetics/br/cid_653_athena_commando_f_uglysweaterfrozen/smallicon.png",
            Rarity = "Epic",
            Series = "FrozenSeries"
        },
        new SaturnItem
        {
            ItemDefinition = "CID_A_311_Athena_Commando_F_ScholarFestiveWinter",
            Name = "Blizzabelle",
            Description = "Voted Teen Queen of Winterfest by a jury of her witchy peers.",
            Icon =
                "https://fortnite-api.com/images/cosmetics/br/cid_a_311_athena_commando_f_scholarfestivewinter/smallicon.png",
            Rarity = "Rare"
        },
        new SaturnItem
        {
            ItemDefinition = "CID_A_007_Athena_Commando_F_StreetFashionEclipse",
            Name = "Ruby Shadows",
            Description = "Sometimes you gotta go dark.",
            Icon =
                "https://fortnite-api.com/images/cosmetics/br/cid_a_007_athena_commando_f_streetfashioneclipse/smallicon.png",
            Rarity = "Epic",
            Series = "ShadowSeries"
        },
        new SaturnItem
        {
            ItemDefinition = "CID_936_Athena_Commando_F_RaiderSilver",
            Name = "Diamond Diva",
            Description = "Synthetic diamonds need not apply.",
            Icon =
                "https://fortnite-api.com/images/cosmetics/br/cid_936_athena_commando_f_raidersilver/smallicon.png",
            Rarity = "Rare"
        },
        new SaturnItem
        {
            ItemDefinition = "CID_294_Athena_Commando_F_RedKnightWinter",
            Name = "Frozen Red Knight",
            Description = "Frozen menace of icy tundra.",
            Icon =
                "https://fortnite-api.com/images/cosmetics/br/cid_294_athena_commando_f_redknightwinter/smallicon.png",
            Rarity = "Legendary",
            Series = "FrozenSeries"
        }
    };
    public async Task<Cosmetic> AddSkinOptions(Cosmetic skin, ISwapperService swapperService, DefaultFileProvider _provider)
    {
        var characterParts = await Task.Run(() => swapperService.GetCharacterPartsById(skin.Id, skin));

        if (characterParts == new Dictionary<string, string>())
            return null;

        foreach (var option in SkinOptions)
        {
            MeshDefaultModel swapModel = new()
            {
                HeadMaterials = new Dictionary<int, string>(),
                HeadHairColor = "/Game/Tamely",
                HeadFX = "/Game/Tamely",
                HeadSkinColor = "/Game/Tamely",
                HeadPartModifierBP = "/Game/Tamely",
                HeadMesh = "/Game/Tamely",
                HeadABP = null,
                BodyFX = "/Game/Tamely",
                BodyPartModifierBP = "/Game/Tamely",
                BodyABP = null,
                BodyMesh = "/Game/Tamely",
                BodyMaterials = new Dictionary<int, string>(),
                BodySkeleton = "/Game/Tamely",
                FaceACCMaterials = new Dictionary<int, string>(),
                FaceACCMesh = "/Game/Tamely",
                FaceACCABP = null,
                FaceACCFX = "/Game/Tamely",
                FaceACCPartModifierBP = "/Game/Tamely",
                HatType = ECustomHatType.ECustomHatType_None
            };

            Dictionary<string, string> MaterialReplacements = new Dictionary<string, string>();
            await Task.Run(() =>
            {
                if (skin is { VariantChannel: { } })
                {
                    if (skin.VariantChannel.ToLower() != "material" &&
                        skin.VariantChannel.ToLower() != "parts" &&
                        skin.VariantTag != null ||
                        !_provider.TryLoadObject(Constants.CidPath + skin.Id, out var CharacterItemDefinition) ||
                        !CharacterItemDefinition.TryGetValue(out UObject[] ItemVariants, "ItemVariants"))
                        return;
                    foreach (var style in ItemVariants)
                    {
                        if (style.TryGetValue(out FStructFallback[] PartOptions, "PartOptions"))
                            foreach (var PartOption in PartOptions)
                            {
                                if (PartOption.TryGetValue(out FText VariantName, "VariantName"))
                                {
                                    Logger.Log("Found Item: " + VariantName.Text);
                                    if (VariantName.Text != skin.Name && skin.VariantTag != null)
                                    {
                                        continue;
                                    }
                                    


                                    if (PartOption.TryGetValue(out FStructFallback[] VariantMaterials,
                                            "VariantMaterials"))
                                        foreach (var variantMaterial in VariantMaterials)
                                        {
                                            var matOverride = variantMaterial.Get<FSoftObjectPath>("OverrideMaterial")
                                                .AssetPathName.Text;
                                            var MaterialToSwap = variantMaterial.Get<FSoftObjectPath>("MaterialToSwap")
                                                .AssetPathName.Text;
                                            
                                            MaterialReplacements.Add(MaterialToSwap, matOverride);
                                        }
                                }
                                else
                                {
                                    Logger.Log("No VariantName found");
                                }
                            }
                        else
                            Logger.Log("No PartOptions found");


                        if (style.TryGetValue(out FStructFallback[] MaterialOptions, "MaterialOptions"))
                            foreach (var MaterialOption in MaterialOptions)
                            {
                                if (MaterialOption.TryGetValue(out FText VariantName, "VariantName"))
                                {
                                    if (VariantName.Text != skin.Name && skin.VariantTag != null)
                                    {
                                        continue;
                                    }


                                    if (MaterialOption.TryGetValue(out FStructFallback[] VariantMaterials,
                                            "VariantMaterials"))
                                        foreach (var variantMaterial in VariantMaterials)
                                        {
                                            var matOverride = variantMaterial.Get<FSoftObjectPath>("OverrideMaterial")
                                                .AssetPathName.Text;
                                            var MaterialToSwap = variantMaterial.Get<FSoftObjectPath>("MaterialToSwap")
                                                .AssetPathName.Text;
                                            
                                            MaterialReplacements.Add(MaterialToSwap, matOverride);
                                        }
                                }
                                else
                                {
                                    Logger.Log("No VariantName found");
                                }
                            }
                        else
                            Logger.Log("No MaterialOptions found");
                    }
                }
            });

            Dictionary<int, string> OGHeadMaterials = new();
            Dictionary<int, string> OGBodyMaterials = new();
            Dictionary<int, string> OGFaceACCMaterials = new();
            bool bDontProceed = false;
            var optionsParts = await Task.Run(() => swapperService.GetCharacterPartsById(option.ItemDefinition));

            await Task.Run(() =>
            {
                if (optionsParts.ContainsKey("Head"))
                {
                    if (!_provider.TryLoadObject(optionsParts["Head"].Split('.')[0], out var part) ||
                        !part.TryGetValue(out FStructFallback[] MaterialOverride, "MaterialOverrides")) return;
                    foreach (var (material, matIndex) in from materialOverride in MaterialOverride
                             let material = materialOverride.Get<FSoftObjectPath>("OverrideMaterial").AssetPathName
                                 .ToString()
                             let matIndex = materialOverride.Get<int>("MaterialOverrideIndex")
                             select (material, matIndex))
                    {
                        OGHeadMaterials.Add(matIndex, material);
                    }
                }

                if (optionsParts.ContainsKey("Face"))
                {
                    if (!_provider.TryLoadObject(optionsParts["Face"].Split('.')[0], out var part) ||
                        !part.TryGetValue(out FStructFallback[] MaterialOverride, "MaterialOverrides")) return;
                    foreach (var (material, matIndex) in from materialOverride in MaterialOverride
                             let material = materialOverride.Get<FSoftObjectPath>("OverrideMaterial").AssetPathName
                                 .ToString()
                             let matIndex = materialOverride.Get<int>("MaterialOverrideIndex")
                             select (material, matIndex))
                    {
                        OGFaceACCMaterials.Add(matIndex, material);
                    }
                }
                
                if (optionsParts.ContainsKey("Hat"))
                {
                    if (!_provider.TryLoadObject(optionsParts["Hat"].Split('.')[0], out var part) ||
                        !part.TryGetValue(out FStructFallback[] MaterialOverride, "MaterialOverrides")) return;
                    foreach (var (material, matIndex) in from materialOverride in MaterialOverride
                             let material = materialOverride.Get<FSoftObjectPath>("OverrideMaterial").AssetPathName
                                 .ToString()
                             let matIndex = materialOverride.Get<int>("MaterialOverrideIndex")
                             select (material, matIndex))
                    {
                        OGFaceACCMaterials.Add(matIndex, material);
                    }
                }
                
                if (optionsParts.ContainsKey("Body"))
                {
                    if (!_provider.TryLoadObject(optionsParts["Body"].Split('.')[0], out var part) ||
                        !part.TryGetValue(out FStructFallback[] MaterialOverride, "MaterialOverrides")) return;
                    foreach (var (material, matIndex) in from materialOverride in MaterialOverride
                             let material = materialOverride.Get<FSoftObjectPath>("OverrideMaterial").AssetPathName
                                 .ToString()
                             let matIndex = materialOverride.Get<int>("MaterialOverrideIndex")
                             select (material, matIndex))
                    {
                        OGBodyMaterials.Add(matIndex, material);
                    }
                }

            });
            
            foreach (var characterPart in characterParts)
            {
                switch (characterPart.Key)
                {
                    case "Body":
                        await Task.Run(() =>
                        {
                            if (_provider.TryLoadObject(characterPart.Value.Split('.')[0], out var part))
                            {
                                if (part.TryGetValue(out FSoftObjectPath mesh, "SkeletalMesh"))
                                    swapModel.BodyMesh = mesh.AssetPathName.Text;

                                swapModel.BodySkeleton =
                                    part.Get<FSoftObjectPath[]>("MasterSkeletalMeshes")[0].AssetPathName.ToString();

                                if (part.TryGetValue(out UObject AdditionalData, "AdditionalData"))
                                {
                                    FSoftObjectPath AnimClass = AdditionalData.GetOrDefault("AnimClass",
                                        new FSoftObjectPath(), StringComparison.OrdinalIgnoreCase);
                                    swapModel.BodyABP = AnimClass.AssetPathName.ToString();
                                }


                                if (part.TryGetValue(out FStructFallback[] MaterialOverride, "MaterialOverrides"))
                                {
                                    foreach (var materialOverride in MaterialOverride)
                                    {
                                        var material = materialOverride.Get<FSoftObjectPath>("OverrideMaterial")
                                            .AssetPathName.ToString();

                                        if (MaterialReplacements.ContainsKey(material))
                                        {
                                            string temp = material;
                                            material = MaterialReplacements[material];
                                            MaterialReplacements.Remove(temp);
                                        }

                                        var matIndex = materialOverride.Get<int>("MaterialOverrideIndex");
                                        if (!swapModel.BodyMaterials.ContainsKey(matIndex))
                                            swapModel.BodyMaterials.Add(matIndex, material);
                                    }
                                }

                                if (swapModel.BodyMaterials.Count > OGBodyMaterials.Count)
                                    bDontProceed = true;

                                swapModel.BodyFX =
                                    part.TryGetValue(out FSoftObjectPath IdleEffectNiagara, "IdleEffectNiagara")
                                        ? IdleEffectNiagara.AssetPathName.ToString()
                                        : "/";

                                if (part.TryGetValue(out FSoftObjectPath IdleEffect, "IdleEffect") &&
                                    swapModel.BodyFX == "/")
                                    swapModel.BodyFX = IdleEffect.AssetPathName.ToString();

                                if (part.TryGetValue(out FSoftObjectPath BodyPartModifierBP, "PartModifierBlueprint"))
                                    swapModel.BodyPartModifierBP = BodyPartModifierBP.AssetPathName.ToString();
                            }
                        });
                        break;

                    case "Head":
                        await Task.Run(() =>
                        {
                            if (_provider.TryLoadObject(characterPart.Value.Split('.')[0], out var part))
                            {
                                swapModel.HeadMesh = part.Get<FSoftObjectPath>("SkeletalMesh").AssetPathName.Text;

                                if (part.TryGetValue(out UObject AdditionalData, "AdditionalData"))
                                {
                                    if (AdditionalData.TryGetValue(out FSoftObjectPath AnimClass, "AnimClass"))
                                        swapModel.HeadABP = AnimClass.AssetPathName.Text;

                                    swapModel.HeadHairColor =
                                        AdditionalData.TryGetValue(out FSoftObjectPath HairColorSwatch,
                                            "HairColorSwatch")
                                            ? HairColorSwatch.AssetPathName.Text
                                            : "/";

                                    swapModel.HeadSkinColor =
                                        AdditionalData.TryGetValue(out FSoftObjectPath SkinColorSwatch,
                                            "SkinColorSwatch")
                                            ? SkinColorSwatch.AssetPathName.Text
                                            : "/";
                                }


                                if (part.TryGetValue(out FStructFallback[] MaterialOverride, "MaterialOverrides"))
                                {
                                    foreach (var materialOverride in MaterialOverride)
                                    {
                                        var material = materialOverride.Get<FSoftObjectPath>("OverrideMaterial")
                                            .AssetPathName.Text;

                                        if (MaterialReplacements.ContainsKey(material))
                                        {
                                            string temp = material;
                                            material = MaterialReplacements[material];
                                            MaterialReplacements.Remove(temp);
                                        }

                                        var matIndex = materialOverride.Get<int>("MaterialOverrideIndex");
                                        if (!swapModel.HeadMaterials.ContainsKey(matIndex))
                                            swapModel.HeadMaterials.Add(matIndex, material);
                                    }
                                }
                                
                                if (OGHeadMaterials.Count < swapModel.HeadMaterials.Count)
                                    bDontProceed = true;

                                swapModel.HeadFX =
                                    part.TryGetValue(out FSoftObjectPath IdleEffectNiagara, "IdleEffectNiagara")
                                        ? IdleEffectNiagara.AssetPathName.ToString()
                                        : "/";

                                if (part.TryGetValue(out FSoftObjectPath IdleEffect, "IdleEffect") &&
                                    swapModel.HeadFX == "/")
                                    swapModel.HeadFX = IdleEffect.AssetPathName.Text;

                                if (part.TryGetValue(out FSoftObjectPath BodyPartModifierBP, "PartModifierBlueprint"))
                                    swapModel.HeadPartModifierBP = BodyPartModifierBP.AssetPathName.Text;
                            }
                        });
                        break;

                    case "Face":
                    case "Hat":
                        await Task.Run(() =>
                        {
                            if (_provider.TryLoadObject(characterPart.Value.Split('.')[0], out var part))
                            {
                                swapModel.FaceACCMesh = part.Get<FSoftObjectPath>("SkeletalMesh").AssetPathName.Text;

                                // This is for skins like ghoul trooper and maven
                                if (swapModel.FaceACCMesh.ToLower().Contains("glasses"))
                                {
                                    swapModel.FaceACCMesh = "/";
                                    swapModel.HatType = ECustomHatType.ECustomHatType_None;
                                    return;
                                }

                                if (part.TryGetValue(out UObject AdditionalData, "AdditionalData"))
                                {
                                    swapModel.FaceACCABP = AdditionalData.GetOrDefault("AnimClass",
                                        new FSoftObjectPath(),
                                        StringComparison.OrdinalIgnoreCase).AssetPathName.Text;

                                    swapModel.HatType = AdditionalData.GetOrDefault("HatType",
                                        ECustomHatType.ECustomHatType_None, StringComparison.OrdinalIgnoreCase);
                                }

                                if (part.TryGetValue(out FStructFallback[] MaterialOverride, "MaterialOverrides"))
                                {
                                    foreach (var materialOverride in MaterialOverride)
                                    {
                                        var material = materialOverride.Get<FSoftObjectPath>("OverrideMaterial")
                                            .AssetPathName.ToString();

                                        if (MaterialReplacements.ContainsKey(material))
                                        {
                                            string temp = material;
                                            material = MaterialReplacements[material];
                                            MaterialReplacements.Remove(temp);
                                        }

                                        var matIndex = materialOverride.Get<int>("MaterialOverrideIndex");
                                        
                                        if (!swapModel.FaceACCMaterials.ContainsKey(matIndex))
                                            swapModel.FaceACCMaterials.Add(matIndex, material);
                                    }
                                }

                                if (swapModel.FaceACCMaterials.Count > OGFaceACCMaterials.Count)
                                    bDontProceed = true;


                                swapModel.FaceACCFX =
                                    part.TryGetValue(out FSoftObjectPath IdleEffectNiagara, "IdleEffectNiagara")
                                        ? IdleEffectNiagara.AssetPathName.ToString()
                                        : "/";

                                if (part.TryGetValue(out FSoftObjectPath IdleEffect, "IdleEffect") &&
                                    swapModel.FaceACCFX == "/")
                                    swapModel.FaceACCFX = IdleEffect.AssetPathName.Text;

                                if (part.TryGetValue(out FSoftObjectPath FaceACCPartModifierBP,
                                        "PartModifierBlueprint"))
                                    swapModel.FaceACCPartModifierBP = FaceACCPartModifierBP.AssetPathName.Text;
                            }
                        });
                        break;
                }
            }

            if (bDontProceed)
            {
                Logger.Log($"Dont proceed adding option \"{option.Name}\" to item \"{skin.Name}\"");
                continue;
            }

            foreach (var (material, value) in MaterialReplacements)
            {
                if (material.ToLower().Contains("hat") || material.ToLower().Contains("helmet") ||
                    material.ToLower().Contains("faceacc") || material.ToLower().Contains("mask"))
                {
                    int i = 0;
                    while (swapModel.FaceACCMaterials.ContainsKey(i)) i++;
                    swapModel.FaceACCMaterials.Add(i, value);
                }
                else if (material.ToLower().Contains("head") || material.ToLower().Contains("hair"))
                {
                    int i = 0;
                    while (swapModel.HeadMaterials.ContainsKey(i)) i++;
                    swapModel.HeadMaterials.Add(i, value);
                }
                else if (material.ToLower().Contains("body") || material.ToLower().Contains("bodies"))
                {
                    int i = 0;
                    while (swapModel.BodyMaterials.ContainsKey(i)) i++;
                    swapModel.BodyMaterials.Add(i, value);
                }
            }
            
            if ((swapModel.HeadMesh.ToLower().Contains("ramirez")) &&
                !swapModel.HeadMesh.ToLower().Contains("/parts/"))
            {
                foreach (var material in swapModel.HeadMaterials)
                {
                    if (!material.Value.ToLower().Contains("hair") ||
                        !OGHeadMaterials[material.Key].ToLower().Contains("hair") ||
                        material.Value.ToLower().Contains("hide")) continue;
                    foreach (var ogMaterial in OGHeadMaterials.Where(ogMaterial
                                 => ogMaterial.Value.ToLower().Contains("hair")))
                    {
                        (swapModel.HeadMaterials[material.Key], swapModel.HeadMaterials[ogMaterial.Key]) = (
                            swapModel.HeadMaterials[ogMaterial.Key], swapModel.HeadMaterials[material.Key]);
                    }
                }
            }

            if (option.Name == "Blizzabelle")
            {
                if (swapModel.HeadMaterials.Count > 1 && swapModel.FaceACCMaterials.Count < 2)
                {
                    (swapModel.FaceACCMesh, swapModel.HeadMesh) = (swapModel.HeadMesh, swapModel.FaceACCMesh);
                    (swapModel.FaceACCABP, swapModel.HeadABP) = (swapModel.HeadABP, swapModel.FaceACCABP);
                    (swapModel.FaceACCMaterials, swapModel.HeadMaterials) =
                        (swapModel.HeadMaterials, swapModel.FaceACCMaterials);
                    (swapModel.HeadFX, swapModel.FaceACCFX) = (swapModel.FaceACCFX, swapModel.HeadFX);
                    (swapModel.HeadPartModifierBP, swapModel.FaceACCPartModifierBP) = (swapModel.FaceACCPartModifierBP,
                        swapModel.HeadPartModifierBP);
                }
            }

            if (swapModel.BodyMaterials == new Dictionary<int, string>() || swapModel.BodyMaterials.Count < 5)
                for (int i = swapModel.BodyMaterials.Count; i < 5; i++)
                {
                    while (swapModel.BodyMaterials.ContainsKey(i)) i++;
                    swapModel.BodyMaterials.Add(i, "/");
                }
            

            if (swapModel.HeadMaterials == new Dictionary<int, string>() || swapModel.HeadMaterials.Count < 5)
                for (int i = swapModel.HeadMaterials.Count; i < 5; i++)
                {
                    while (swapModel.HeadMaterials.ContainsKey(i)) i++;
                    swapModel.HeadMaterials.Add(i, "/");
                }


            if (swapModel.FaceACCMaterials == new Dictionary<int, string>() || swapModel.FaceACCMaterials.Count < 5)
                for (int i = swapModel.FaceACCMaterials.Count; i < 5; i++)
                {
                    while (swapModel.FaceACCMaterials.ContainsKey(i)) i++;
                    swapModel.FaceACCMaterials.Add(i, "/");
                }

            if (swapModel.FaceACCABP == "None")
                swapModel.FaceACCABP = null;
            if (swapModel.HeadABP == "None")
                swapModel.HeadABP = null;
            if (swapModel.BodyABP == "None")
                swapModel.BodyABP = null;

            if (string.IsNullOrEmpty(swapModel.BodyABP))
                swapModel.BodyABP = null;
            if (string.IsNullOrEmpty(swapModel.HeadABP))
                swapModel.HeadABP = null;
            if (string.IsNullOrEmpty(swapModel.FaceACCABP))
                swapModel.FaceACCABP = null;

            option.SwapModel = swapModel;
            skin.CosmeticOptions.Add(option);
        }




        return skin;
    }
}
