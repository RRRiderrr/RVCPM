using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RVCPM
{
    public sealed class AppConfig
    {
        public int SchemaVersion { get; set; } = 2;
        public string Language { get; set; } = "en";
        public string DiscordBranch { get; set; } = "auto";
        public string CustomDiscordLocation { get; set; } = "";
        public bool AutoUpdateVencordBeforeBuild { get; set; } = true;
        public bool AutoRestartAfterInstall { get; set; } = true;
        public bool EnablePluginsAfterInstall { get; set; } = true;
        public bool DevBuild { get; set; } = false;
        public bool PendingRestart { get; set; } = false;
        public bool PendingBuildChanges { get; set; } = false;
        public DateTime? LastBuildUtc { get; set; }
        public string LastVencordCommit { get; set; } = "";
        public string LastVencordVersion { get; set; } = "";
        public List<ManagedPlugin> Plugins { get; set; } = new List<ManagedPlugin>();
        public List<ManagedRepository> Repositories { get; set; } = new List<ManagedRepository>();
        public Dictionary<string, JObject> PendingPluginSettings { get; set; } = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        public List<ManagedPlugin> PendingPackageCleanup { get; set; } = new List<ManagedPlugin>();
    }

    public enum PluginSourceKind
    {
        LocalFile,
        LocalFolder,
        Zip,
        GitHub,
        DropSnapshot
    }

    public sealed class ManagedPlugin
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string PluginDescription { get; set; } = "";
        public string Author { get; set; } = "";
        public string Version { get; set; } = "";
        public PluginSourceKind SourceKind { get; set; } = PluginSourceKind.LocalFile;
        public string SourceReference { get; set; } = "";
        public string PackagePath { get; set; } = "";
        public string RepositoryId { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public bool SourceIsFile { get; set; }
        public string EntryExtension { get; set; } = ".ts";
        public string TargetSuffix { get; set; } = "";
        public string TargetFolder { get; set; } = "";
        public string ContentHash { get; set; } = "";
        public string LastKnownCommit { get; set; } = "";
        public bool UpdateAvailable { get; set; }
        public bool EnabledByDefault { get; set; }
        public bool Required { get; set; }
        public bool RequiresRestart { get; set; }
        public bool HasSettings { get; set; }
        public string GitHubUrl { get; set; } = "";
        public string GitHubDescription { get; set; } = "";
        public string Readme { get; set; } = "";
        public DateTime InstalledUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastUpdatedUtc { get; set; }
        public List<string> Dependencies { get; set; } = new List<string>();
        public List<PluginSettingSchema> Settings { get; set; } = new List<PluginSettingSchema>();
    }

    public sealed class ManagedRepository
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Url { get; set; } = "";
        public string Owner { get; set; } = "";
        public string Name { get; set; } = "";
        public string Branch { get; set; } = "main";
        public string LocalPath { get; set; } = "";
        public string Commit { get; set; } = "";
        public string Description { get; set; } = "";
        public string Homepage { get; set; } = "";
        public string Readme { get; set; } = "";
        public DateTime? LastCheckedUtc { get; set; }
    }

    public sealed class PluginCandidate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string Version { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public bool IsFile { get; set; }
        public string Extension { get; set; } = ".ts";
        public string TargetSuffix { get; set; } = "";
        public PluginSourceKind SourceKind { get; set; } = PluginSourceKind.LocalFile;
        public string OriginReference { get; set; } = "";
        public bool EnabledByDefault { get; set; }
        public bool Required { get; set; }
        public bool RequiresRestart { get; set; }
        public List<string> Dependencies { get; set; } = new List<string>();
        public List<PluginSettingSchema> Settings { get; set; } = new List<PluginSettingSchema>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class CandidateBatch
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public PluginSourceKind SourceKind { get; set; }
        public string SourceReference { get; set; } = "";
        public string TempRoot { get; set; } = "";
        public string RepositoryId { get; set; } = "";
        public List<PluginCandidate> Candidates { get; set; } = new List<PluginCandidate>();
    }

    public enum PluginSettingType
    {
        String,
        Number,
        Boolean,
        Select,
        Slider,
        BigInt,
        Component,
        Custom,
        Unknown
    }

    public sealed class PluginSettingSchema
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public PluginSettingType Type { get; set; }
        public JToken DefaultValue { get; set; }
        public bool RestartNeeded { get; set; }
        public bool UnsupportedOutsideDiscord { get; set; }
        // Vencord's OptionType.CUSTOM is storage, not a generic settings control.
        // COMPONENT is user-facing, but its React component can only run inside Discord.
        public bool UserFacing { get; set; } = true;
        public bool EditableInManager { get; set; } = true;
        public bool Hidden { get; set; }
        public bool Disabled { get; set; }
        public bool ConditionalVisibility { get; set; }
        public bool ConditionalDisabled { get; set; }
        public string Placeholder { get; set; } = "";
        public bool Multiline { get; set; }
        public bool StickToMarkers { get; set; }
        public List<PluginSettingOption> Options { get; set; } = new List<PluginSettingOption>();
        public List<double> Markers { get; set; } = new List<double>();
    }

    public sealed class PluginSettingOption
    {
        public string Label { get; set; } = "";
        public JToken Value { get; set; }
        public bool IsDefault { get; set; }
    }

    public sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
        public bool Success { get { return ExitCode == 0; } }
    }

    public sealed class OperationProgress
    {
        public string Stage { get; set; } = "";
        public string Message { get; set; } = "";
        public int Percent { get; set; } = -1;
        public bool CanCancel { get; set; } = true;
    }
}
