# escape=`
# .NET Framework 4.8 requires Windows containers.
# Build this image on a Windows host or via GitHub Actions (windows-latest runner).

# --- Build Stage ---
FROM mcr.microsoft.com/dotnet/framework/sdk:4.8-windowsservercore-ltsc2022 AS build
WORKDIR C:\build

# Restore NuGet packages first for better layer caching
COPY src\RyanairPayments\packages.config .
RUN nuget restore packages.config -PackagesDirectory packages

# Copy full source and compile
COPY src\RyanairPayments\ .
RUN msbuild RyanairPayments.csproj /p:Configuration=Release /p:OutputPath=C:\out /p:DebugType=None /p:DebugSymbols=false /nologo /v:minimal

# --- Runtime Stage ---
FROM mcr.microsoft.com/dotnet/framework/aspnet:4.8-windowsservercore-ltsc2022
LABEL maintainer="aaronkinchen" `
      description="Ryanair Payments Simulator - .NET Framework 4.8 ASP.NET Web API" `
      version="1.0.0"

WORKDIR C:\inetpub\wwwroot

# Copy compiled binaries and web application files
COPY --from=build C:\out\           bin\
COPY --from=build C:\build\Web.config  .
COPY --from=build C:\build\Global.asax .

# Expose IIS default port
EXPOSE 80

HEALTHCHECK --interval=30s --timeout=10s --start-period=90s --retries=3 `
    CMD powershell -NoProfile -Command `
        "try { $r = Invoke-WebRequest -Uri http://localhost/api/health -UseBasicParsing -TimeoutSec 8; if ($r.StatusCode -eq 200) { exit 0 } else { exit 1 } } catch { exit 1 }"
