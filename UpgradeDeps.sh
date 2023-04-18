pushd ./Unitee.EventDriven.RedisStream.Tests
dotnet outdated -u
dotnet build
popd

pushd ./Unitee.EventDriven.AzureServiceBus
dotnet outdated -u
dotnet build
popd

pushd ./Unitee.EventDriven.RedisStream
dotnet outdated -u
dotnet build
popd

pushd ./Unitee.EventDriven.Abstraction
dotnet outdated -u
dotnet build
popd

