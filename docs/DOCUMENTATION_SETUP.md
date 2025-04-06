# Setting Up DocFX for Lidarr Tidal Plugin

This guide explains how to set up DocFX to generate comprehensive documentation for the Lidarr Tidal Plugin.

## Prerequisites

- .NET SDK 6.0 or later
- Visual Studio or VS Code with C# extension
- Git

## Installation

1. Install DocFX as a global tool:

```powershell
dotnet tool install -g docfx
```

## Project Setup

1. Create a `docfx_project` folder in the project root:

```powershell
mkdir docfx_project
cd docfx_project
docfx init -q
```

2. Configure `docfx.json` to target your project:

```json
{
  "metadata": [
    {
      "src": [
        {
          "files": [
            "src/**.csproj"
          ],
          "src": ".."
        }
      ],
      "dest": "api",
      "disableGitFeatures": false,
      "disableDefaultFilter": false
    }
  ],
  "build": {
    "content": [
      {
        "files": [
          "api/**.yml",
          "api/index.md"
        ]
      },
      {
        "files": [
          "articles/**.md",
          "articles/**/toc.yml",
          "architecture/**.md",
          "architecture/**/toc.yml",
          "guides/**.md",
          "guides/**/toc.yml",
          "toc.yml",
          "*.md"
        ]
      }
    ],
    "resource": [
      {
        "files": [
          "images/**"
        ]
      }
    ],
    "overwrite": [
      {
        "files": [
          "apidoc/**.md"
        ],
        "exclude": [
          "obj/**",
          "_site/**"
        ]
      }
    ],
    "dest": "_site",
    "globalMetadataFiles": [],
    "fileMetadataFiles": [],
    "template": [
      "default",
      "templates/lidarr"
    ],
    "postProcessors": [],
    "markdownEngineName": "markdig",
    "noLangKeyword": false,
    "keepFileLink": false,
    "cleanupCacheHistory": false,
    "disableGitFeatures": false
  }
}
```

3. Create documentation structure:

```
docfx_project/
├── api/               # API documentation (auto-generated)
├── articles/          # Conceptual documentation
│   ├── intro.md       # Introduction to the plugin
│   └── toc.yml        # Table of contents for articles
├── architecture/      # Architecture documentation
│   ├── overview.md    # System architecture overview
│   └── toc.yml        # Table of contents for architecture
├── guides/            # User and developer guides
│   ├── user-guide.md  # User guide
│   ├── dev-guide.md   # Developer guide
│   └── toc.yml        # Table of contents for guides
├── images/            # Documentation images
├── index.md           # Home page
└── toc.yml            # Main table of contents
```

4. Create a custom template folder (optional):

```powershell
mkdir -p templates/lidarr
```

## XML Documentation

Ensure your code has proper XML documentation:

```csharp
/// <summary>
/// Manages a thread-safe queue of download tasks for Tidal content.
/// </summary>
public class DownloadTaskQueue : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the DownloadTaskQueue class.
    /// </summary>
    /// <param name="capacity">The capacity of the download queue.</param>
    /// <param name="settings">The Tidal settings.</param>
    /// <param name="logger">The logger to use.</param>
    public DownloadTaskQueue(int capacity, TidalSettings settings, Logger logger)
    {
        // Implementation
    }
}
```

## Building Documentation

Run these commands to build the documentation:

```powershell
cd docfx_project
docfx metadata
docfx build
```

View the documentation locally:

```powershell
docfx serve _site
```

## Continuous Integration

Add a GitHub Actions workflow to automatically build and deploy your documentation:

Create `.github/workflows/docs.yml`:

```yaml
name: docs

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v2
    - name: Setup .NET
      uses: actions/setup-dotnet@v1
      with:
        dotnet-version: 6.0.x
    - name: Install DocFX
      run: dotnet tool install -g docfx
    - name: Build Documentation
      run: |
        cd docfx_project
        docfx metadata
        docfx build
    - name: Deploy to GitHub Pages
      if: github.event_name == 'push' && github.ref == 'refs/heads/main'
      uses: peaceiris/actions-gh-pages@v3
      with:
        github_token: ${{ secrets.GITHUB_TOKEN }}
        publish_dir: ./docfx_project/_site
```

## Best Practices

1. **Keep Code and Docs in Sync**: Update documentation when updating code.
2. **Use Diagrams**: Add PlantUML or Mermaid diagrams to explain complex workflows.
3. **Add Examples**: Include code examples for common scenarios.
4. **Versioning**: Maintain documentation for different versions of your plugin.
5. **Review**: Regularly review documentation for accuracy.

## Troubleshooting

- **Missing XML Comments**: Run `dotnet build /p:DocumentationFile=bin\Debug\netX.X\YourProject.xml` to identify missing documentation.
- **Build Errors**: Check the DocFX log output for issues with documentation format.
- **Broken Links**: Use the link validator in DocFX to find broken links.

## Resources

- [DocFX documentation](https://dotnet.github.io/docfx/)
- [C# XML Documentation Guide](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/)
- [Markdown Guide](https://www.markdownguide.org/) 