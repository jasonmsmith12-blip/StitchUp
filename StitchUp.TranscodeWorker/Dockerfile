# -------- Build stage --------
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY . .
RUN dotnet publish StitchUp.TranscodeWorker/StitchUp.TranscodeWorker.csproj -c Release -o /out

# -------- Runtime stage --------
FROM mcr.microsoft.com/dotnet/runtime:10.0-preview AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/ .

ENTRYPOINT ["dotnet", "StitchUp.TranscodeWorker.dll"]