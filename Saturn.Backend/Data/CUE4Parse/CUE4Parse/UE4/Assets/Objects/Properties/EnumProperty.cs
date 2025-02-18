﻿using System;
using System.Collections.Generic;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.Utils;
using Newtonsoft.Json;
using Saturn.Backend.Data;

namespace CUE4Parse.UE4.Assets.Objects
{
    [JsonConverter(typeof(EnumPropertyConverter))]
    public class EnumProperty : FPropertyTagType<FName>
    {
        private int index = 0;
        private ReadType type;
        public EnumProperty(FAssetArchive Ar, FPropertyTagData? tagData, ReadType type)
        {
            this.type = type;
            if (type == ReadType.ZERO)
            {
                Value = new FName(IndexToEnum(Ar, tagData, 0));
            }
            else if (Ar.HasUnversionedProperties && type == ReadType.NORMAL)
            {
                if (tagData?.InnerType != null)
                {
                    var underlyingProp = ReadPropertyTagType(Ar, tagData.InnerType, tagData.InnerTypeData, ReadType.NORMAL)?.GenericValue;
                    if (underlyingProp != null && underlyingProp.IsNumericType())
                        index = Convert.ToInt32(underlyingProp);
                }
                else
                {
                    index = Ar.Read<byte>();
                }
                Value = new FName(IndexToEnum(Ar, tagData, index));
            }
            else
            {
                Value = Ar.ReadFName();
            }
        }

        public override void Serialize(List<byte> Ar)
        {
            Ar.Add((byte)index);
        }

        private static string IndexToEnum(FAssetArchive Ar, FPropertyTagData? tagData, int index)
        {
            var enumName = tagData?.EnumName;
            if (enumName == null)
                return index.ToString();

            if (tagData.Enum != null) // serialized
            {
                foreach (var (name, value) in tagData.Enum.Names)
                    if (value == index)
                        return name.Text;

                return string.Concat(enumName, "::", index);
            }

            if (Ar.Owner.Mappings != null &&
                Ar.Owner.Mappings.Enums.TryGetValue(enumName, out var values) &&
                values.TryGetValue(index, out var member))
            {
                return string.Concat(enumName, "::", member);
            }

            return string.Concat(enumName, "::", index);
        }
    }

    public class EnumPropertyConverter : JsonConverter<EnumProperty>
    {
        public override void WriteJson(JsonWriter writer, EnumProperty value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value.Value);
        }

        public override EnumProperty ReadJson(JsonReader reader, Type objectType, EnumProperty existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}