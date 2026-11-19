FROM docker-prd.registry.energisa.corp/devops-runtime/aspnet-8:latest-jammy

ENV TZ=America/Sao_Paulo
ENV DISABLE_KERNEL_MEMORY_ACCOUNTING=true
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

USER root

COPY / App/
  
WORKDIR /App
ENTRYPOINT ["dotnet", "Energisa.Eforce.Otimizador.Cronjob.dll"]