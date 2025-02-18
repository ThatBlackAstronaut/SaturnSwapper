﻿using System;
using System.Collections.Generic;
using CUE4Parse.UE4.Assets.Readers;
using Newtonsoft.Json;

namespace CUE4Parse.UE4.Assets.Objects
{
    [JsonConverter(typeof(SetPropertyConverter))]
    public class SetProperty : FPropertyTagType<UScriptSet>
    {
        public SetProperty(FAssetArchive Ar, FPropertyTagData? tagData, ReadType type)
        {
            Value = type switch
            {
                ReadType.ZERO => new UScriptSet(),
                _ => new UScriptSet(Ar, tagData)
            };
        }

        public override void Serialize(List<byte> Ar)
        {
            // Skip paths to remove, might break it, we'll see :shrug:
            Ar.AddRange(BitConverter.GetBytes(Value.Properties.Count));
            foreach (var property in Value.Properties)
            {
                property.Serialize(Ar);
            }
        }
    }
    
    public class SetPropertyConverter : JsonConverter<SetProperty>
    {
        public override void WriteJson(JsonWriter writer, SetProperty value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value.Value);
        }

        public override SetProperty ReadJson(JsonReader reader, Type objectType, SetProperty existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}