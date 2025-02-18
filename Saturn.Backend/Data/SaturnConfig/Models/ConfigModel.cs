﻿using System.Collections.Generic;
using Saturn.Backend.Data.Services.OobeServiceUtils;
using Saturn.Backend.Data.Swapper.Swapping.Models;

namespace Saturn.Backend.Data.SaturnConfig.Models
{
    public class ConfigModel
    {
        public string Key { get; set; }
        public string FortniteVersion { get; set; }
        public Dictionary<string, string> DependencyVersions { get; set; } = new();
        public OobeType OobeType { get; set; }
        public List<PresetModel> Presets { get; set; } = new();
    }
}
