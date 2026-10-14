namespace Tidy.Core.Models;

/// <summary>
/// The single source of truth for project artifacts: what identifies the project,
/// which folder is removable, how it comes back, and how risky removing it is.
/// Adding an ecosystem means adding a row here and an explanation in catalog.json.
/// </summary>
public static class ProjectArtifactCatalog
{
    private const string NodeRestore = "npm install (or pnpm install / yarn, whichever the project uses)";

    public static readonly IReadOnlyList<ProjectArtifactRule> Rules =
    [
        // JavaScript / TypeScript
        new() { Kind = "node-modules", ProjectType = "Node", Folder = "node_modules",
            RootMarkers = ["package.json"],
            Lockfiles = ["package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", "bun.lockb", "bun.lock"],
            RebuildMarkers = ["package.json"], Restore = NodeRestore },
        new() { Kind = "next-build", ProjectType = "Node", Folder = ".next", RootMarkers = ["package.json"], RebuildMarkers = ["package.json"], Restore = NodeRestore },
        new() { Kind = "nuxt-build", ProjectType = "Node", Folder = ".nuxt",
            RootMarkers = ["nuxt.config.ts", "nuxt.config.js", "nuxt.config.mjs", "nuxt.config.cjs"], RebuildMarkers = ["package.json"], Restore = NodeRestore },
        // `.output` is the loosest folder name here, so it is anchored on a Nuxt config, not package.json.
        new() { Kind = "nuxt-output", ProjectType = "Node", Folder = ".output",
            RootMarkers = ["nuxt.config.ts", "nuxt.config.js", "nuxt.config.mjs", "nuxt.config.cjs"], RebuildMarkers = ["package.json"], Restore = NodeRestore },
        new() { Kind = "sveltekit-build", ProjectType = "Node", Folder = ".svelte-kit", RootMarkers = ["package.json"], RebuildMarkers = ["package.json"], Restore = NodeRestore },
        new() { Kind = "astro-build", ProjectType = "Node", Folder = ".astro", RootMarkers = ["package.json"], RebuildMarkers = ["package.json"], Restore = NodeRestore },
        new() { Kind = "angular-cache", ProjectType = "Node", Folder = ".angular", RootMarkers = ["package.json"], RebuildMarkers = ["package.json"], Restore = NodeRestore },
        new() { Kind = "turbo-cache", ProjectType = "Node", Folder = ".turbo", RootMarkers = ["package.json"], RebuildMarkers = ["package.json"], Restore = NodeRestore },
        new() { Kind = "parcel-cache", ProjectType = "Node", Folder = ".parcel-cache", RootMarkers = ["package.json"], RebuildMarkers = ["package.json"], Restore = NodeRestore },

        // Rust
        new() { Kind = "rust-target", ProjectType = "Rust", Folder = "target", RootMarkers = ["Cargo.toml"], RebuildMarkers = ["Cargo.toml"], Restore = "cargo build" },

        // Python
        new() { Kind = "venv", ProjectType = "Python", Folder = "venv", RootMarkers = ["requirements.txt", "pyproject.toml"],
            RebuildMarkers = ["requirements.txt", "pyproject.toml"], Restore = "Recreate the environment with your usual steps, for example python -m venv venv and pip install -r requirements.txt." },
        new() { Kind = "venv", ProjectType = "Python", Folder = ".venv", RootMarkers = ["requirements.txt", "pyproject.toml"],
            RebuildMarkers = ["requirements.txt", "pyproject.toml"], Restore = "Recreate the environment with your usual steps, for example uv sync or python -m venv .venv." },
        new() { Kind = "pytest-cache", ProjectType = "Python", Folder = ".pytest_cache", RootMarkers = ["requirements.txt", "pyproject.toml"], Restore = "Recreated the next time you run your tests." },
        new() { Kind = "mypy-cache", ProjectType = "Python", Folder = ".mypy_cache", RootMarkers = ["requirements.txt", "pyproject.toml"], Restore = "Recreated the next time you run mypy." },
        new() { Kind = "ruff-cache", ProjectType = "Python", Folder = ".ruff_cache", RootMarkers = ["requirements.txt", "pyproject.toml"], Restore = "Recreated the next time you run ruff." },
        new() { Kind = "tox-environments", ProjectType = "Python", Folder = ".tox", RootMarkers = ["tox.ini"], Restore = "tox" },

        // Flutter / Dart
        new() { Kind = "dart-tool", ProjectType = "Flutter", Folder = ".dart_tool", RootMarkers = ["pubspec.yaml"], Lockfiles = ["pubspec.lock"], RebuildMarkers = ["pubspec.yaml"], Restore = "flutter pub get" },
        new() { Kind = "flutter-cache", ProjectType = "Flutter", Folder = "build", RootMarkers = ["pubspec.yaml"], Lockfiles = ["pubspec.lock"], RebuildMarkers = ["pubspec.yaml"], Restore = "flutter build" },

        // Android / JVM
        new() { Kind = "gradle-cache", ProjectType = "Android", Folder = ".gradle",
            RootMarkers = ["build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts", @"android\build.gradle", @"android\build.gradle.kts"],
            RebuildMarkers = ["build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts", @"android\build.gradle", @"android\build.gradle.kts"],
            Restore = "Run your usual build so Gradle fetches what it needs again." },
        new() { Kind = "maven-target", ProjectType = "Maven", Folder = "target", RootMarkers = ["pom.xml"], RebuildMarkers = ["pom.xml"], Restore = "mvn package" },
        new() { Kind = "gradle-build", ProjectType = "Gradle", Folder = "build", RootMarkers = ["build.gradle", "build.gradle.kts"], RebuildMarkers = ["build.gradle", "build.gradle.kts"], Restore = @".\gradlew build" },
        new() { Kind = "sbt-target", ProjectType = "sbt", Folder = "target", RootMarkers = ["build.sbt"], RebuildMarkers = ["build.sbt"], Restore = "sbt compile" },
        new() { Kind = "sbt-target", ProjectType = "sbt", Folder = @"project\target", RootMarkers = ["build.sbt"], RebuildMarkers = [@"..\build.sbt"], Restore = "sbt compile" },

        // .NET
        new() { Kind = "dotnet-bin", ProjectType = ".NET", Folder = "bin", RootMarkerExtensions = ["csproj", "fsproj", "vbproj"], Restore = "dotnet build" },
        new() { Kind = "dotnet-obj", ProjectType = ".NET", Folder = "obj", RootMarkerExtensions = ["csproj", "fsproj", "vbproj"], Restore = "dotnet build" },

        // Elixir / Erlang
        new() { Kind = "elixir-build", ProjectType = "Elixir", Folder = "_build", RootMarkers = ["mix.exs"], RebuildMarkers = ["mix.exs"], Restore = "mix compile" },
        new() { Kind = "elixir-deps", ProjectType = "Elixir", Folder = "deps", RootMarkers = ["mix.exs"], Lockfiles = ["mix.lock"], RebuildMarkers = ["mix.exs"], Restore = "mix deps.get" },

        // PHP / Ruby
        new() { Kind = "composer-vendor", ProjectType = "PHP", Folder = "vendor", RootMarkers = ["composer.json"], Lockfiles = ["composer.lock"], RebuildMarkers = ["composer.json"], Restore = "composer install" },
        new() { Kind = "bundler-vendor", ProjectType = "Ruby", Folder = @"vendor\bundle", RootMarkers = ["Gemfile"], Lockfiles = [@"..\Gemfile.lock"], RebuildMarkers = [@"..\Gemfile"], Restore = "bundle install" },

        // Haskell
        new() { Kind = "stack-work", ProjectType = "Haskell", Folder = ".stack-work", RootMarkers = ["stack.yaml"], RebuildMarkers = ["stack.yaml"], Restore = "stack build" },
        new() { Kind = "cabal-dist", ProjectType = "Haskell", Folder = "dist-newstyle", RootMarkerExtensions = ["cabal"], Restore = "cabal build" },

        // Zig / OCaml / native
        new() { Kind = "zig-cache", ProjectType = "Zig", Folder = ".zig-cache", RootMarkers = ["build.zig"], RebuildMarkers = ["build.zig"], Restore = "zig build" },
        new() { Kind = "zig-cache", ProjectType = "Zig", Folder = "zig-cache", RootMarkers = ["build.zig"], RebuildMarkers = ["build.zig"], Restore = "zig build" },
        new() { Kind = "zig-out", ProjectType = "Zig", Folder = "zig-out", RootMarkers = ["build.zig"], RebuildMarkers = ["build.zig"], Restore = "zig build" },
        new() { Kind = "dune-build", ProjectType = "OCaml", Folder = "_build", RootMarkers = ["dune-project"], RebuildMarkers = ["dune-project"], Restore = "dune build" },
        new() { Kind = "cmake-build", ProjectType = "CMake", Folder = "build", RootMarkers = ["CMakeLists.txt"], RebuildMarkers = ["CMakeLists.txt"], Restore = "Run your usual CMake configure and build steps." },

        // Infrastructure / engines.
        // `.terraform` is also where a local backend writes state. A folder holding state
        // is refused outright; the rest is Check First.
        new() { Kind = "terraform-plugins", ProjectType = "Terraform", Folder = ".terraform", RootMarkerExtensions = ["tf"],
            RefuseWhenPresent = ["terraform.tfstate", "terraform.tfstate.backup", "environment"], Restore = "terraform init", Level = SafetyLevel.CheckFirst },
        new() { Kind = "godot-cache", ProjectType = "Godot", Folder = ".godot", RootMarkers = ["project.godot"], RebuildMarkers = ["project.godot"], Restore = "Rebuilt the next time you open the project in Godot." },
        // Unity and Unreal rebuild themselves, but a reimport can take half an hour,
        // so these are Check First and never part of a one-click clean.
        new() { Kind = "unity-library", ProjectType = "Unity", Folder = "Library", RootMarkers = [@"ProjectSettings\ProjectVersion.txt"],
            RebuildMarkers = [@"ProjectSettings\ProjectVersion.txt"], Restore = "Unity reimports the project the next time you open it.", Level = SafetyLevel.CheckFirst },
        new() { Kind = "unity-temp", ProjectType = "Unity", Folder = "Temp", RootMarkers = [@"ProjectSettings\ProjectVersion.txt"],
            Restore = "Recreated the next time you open the project in Unity.", Level = SafetyLevel.CheckFirst },
        new() { Kind = "unreal-intermediate", ProjectType = "Unreal", Folder = "Intermediate", RootMarkerExtensions = ["uproject"],
            Restore = "Regenerated the next time you build the project in Unreal.", Level = SafetyLevel.CheckFirst },
        new() { Kind = "unreal-derived-data", ProjectType = "Unreal", Folder = "DerivedDataCache", RootMarkerExtensions = ["uproject"],
            Restore = "Rebuilt on the next build, including shader compilation.", Level = SafetyLevel.CheckFirst },
    ];

    /// <summary>Folder names any rule can propose; the project walk never descends into these.</summary>
    public static readonly IReadOnlySet<string> ArtifactFolderNames =
        Rules.Select(r => r.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The rules that would propose <paramref name="artifactPath"/>, paired with the
    /// project root they imply. Several rules can share a folder name (<c>target</c> is
    /// Rust, Maven or sbt), so the caller checks which one's root markers are present.
    /// </summary>
    public static IEnumerable<(ProjectArtifactRule Rule, string Root)> Candidates(string artifactPath)
    {
        var segments = artifactPath.Split('\\');
        foreach (var rule in Rules)
        {
            var folderSegments = rule.Folder.Split('\\');
            if (folderSegments.Length >= segments.Length) continue;
            var tail = segments[^folderSegments.Length..];
            if (!tail.SequenceEqual(folderSegments, StringComparer.OrdinalIgnoreCase)) continue;
            var root = string.Join('\\', segments[..^folderSegments.Length]);
            if (root.Length == 2) root += "\\";
            yield return (rule, root);
        }
    }
}
