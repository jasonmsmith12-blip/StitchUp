# -------- Build stage --------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything (simplest first pass)
COPY . .

# Publish the worker
# IMPORTANT: update the .csproj path if your project filename is different
RUN dotnet publish StitchUp.TranscodeWorker/StitchUp.TranscodeWorker.csproj -c Release -o /out

# -------- Runtime stage --------
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

# Install ffmpeg
RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*

# Copy published app from build stage
COPY --from=build /out/ .

# Run
ENTRYPOINT ["dotnet", "StitchUp.TranscodeWorker.dll"]