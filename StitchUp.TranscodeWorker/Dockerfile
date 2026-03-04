FROM mcr.microsoft.com/dotnet/runtime:8.0

RUN apt-get update && \
    apt-get install -y ffmpeg && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY bin/Release/net8.0/publish/ .

ENTRYPOINT ["dotnet", "StitchUp.TranscodeWorker.dll"]