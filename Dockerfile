FROM mcr.microsoft.com/dotnet/sdk:8.0.419

WORKDIR /src

COPY src/DisplayProfileSwitcher/DisplayProfileSwitcher.csproj src/DisplayProfileSwitcher/
COPY images/icon.ico images/icon.ico
RUN dotnet restore src/DisplayProfileSwitcher/DisplayProfileSwitcher.csproj \
    --runtime win-x64 \
    -p:EnableWindowsTargeting=true

COPY src/DisplayProfileSwitcher/ src/DisplayProfileSwitcher/
RUN dotnet publish src/DisplayProfileSwitcher/DisplayProfileSwitcher.csproj \
    --configuration Release \
    --runtime win-x64 \
    --self-contained true \
    --no-restore \
    -p:EnableWindowsTargeting=true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    --output /out

CMD ["test", "-f", "/out/DisplayProfileSwitcher.exe"]
