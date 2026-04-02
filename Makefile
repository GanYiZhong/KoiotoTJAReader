.PHONY: all build release clean kill

# Default: stop Koioto if running, then build Debug
all: kill build

build:
	dotnet build TJAReader.csproj -c Debug

release: kill
	dotnet build TJAReader.csproj -c Release

clean:
	dotnet clean TJAReader.csproj

# Kill Koioto gracefully before building (ignore error if not running)
kill:
	-taskkill /IM Koioto.exe /F 2>nul || true
