using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Dotnet.Bundle
{
    public class BundleAppTask : Task
    {
        [Required]
        public string AppName { get; set; }

        [Required]
        public string SourcesDir { get; set; }

        [Required]
        public string OutDir { get; set; }
        
        [Required]
        public string PublishDir { get; set; }
        
        [Required]
        public string CFBundleName { get; set; }

        [Required]
        public string CFBundleDisplayName { get; set; }

        [Required]
        public string CFBundleIdentifier { get; set; }

        [Required]
        public string CFBundleVersion { get; set; }

        [Required]
        public string CFBundlePackageType { get; set; }

        [Required]
        public string CFBundleSignature { get; set; }

        [Required]
        public string CFBundleExecutable { get; set; }

        [Required]
        public string CFBundleIconFile { get; set; }

        [Required]
        public string CFBundleShortVersionString { get; set; }

        [Required]
        public string CFBundleInfoDictionaryVersion { get; set; }

        [Required]
        public string NSPrincipalClass { get; set; }
        
        public string MDItemKeywords { get; set; }

        public bool NSHighResolutionCapable {
            get => NSHighResolutionCapableNullable.Value;
            set => NSHighResolutionCapableNullable = value;
        }

        internal bool? NSHighResolutionCapableNullable { get; private set; }

        public bool NSRequiresAquaSystemAppearance {
            get => NSRequiresAquaSystemAppearanceNullable.Value;
            set => NSRequiresAquaSystemAppearanceNullable = value;
        }

        internal bool? NSRequiresAquaSystemAppearanceNullable { get; private set; }

        public bool LSUIElement {
            get => LSUIElementNullable.Value;
            set => LSUIElementNullable = value;
        }

        internal bool? LSUIElementNullable { get; private set; }

        public bool LSBackgroundOnly {
            get => LSBackgroundOnlyNullable.Value;
            set => LSBackgroundOnlyNullable = value;
        }

        internal bool? LSBackgroundOnlyNullable { get; private set; }

        public ITaskItem[] CFBundleURLTypes { get; set; }

        public ITaskItem[] LSEnvironment { get; set; }
        
        public string[] CFBundleAlternateNames { get; set; }

        public override bool Execute()
        {
            var builder = new StructureBuilder(this);
            builder.Build();
            
            var bundler = new AppBundler(this, builder);
            bundler.Bundle();
            
            var plistWriter = new PlistWriter(this, builder);
            plistWriter.Write();

            return true;
        }
    }
}
