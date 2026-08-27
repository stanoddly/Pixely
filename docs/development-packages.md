# Development packages

Every validated `main` commit is available from the public Pixely development feed:

```text
https://pixely.pages.dev/index.json
```

The packages use versions in the form `0.0.N`. Although they do not have a suffix, every package obtained from this feed is a development build until that exact version is published to nuget.org.

Add the development feed alongside nuget.org:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="pixely-development" value="https://pixely.pages.dev/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="pixely-development">
      <package pattern="Pixely" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Reference an exact version:

```xml
<PackageReference Include="Pixely" Version="0.0.N" />
```

Do not use a floating Pixely version across the development and nuget.org feeds. Test an unauthenticated restore with a clean package directory:

```bash
packages_directory="$(mktemp -d)"
dotnet restore --configfile NuGet.config --packages "$packages_directory"
dotnet build --no-restore
```
