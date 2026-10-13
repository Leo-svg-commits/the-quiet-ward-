FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY QuietWardServer.csproj QuietWardServer.cs Program.cs JavaScriptSerializerShim.cs ./
RUN dotnet publish QuietWardServer.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /out ./
COPY index.html README.md ./
ENV PORT=8000
EXPOSE 8000
CMD ["dotnet", "QuietWardServer.dll"]
