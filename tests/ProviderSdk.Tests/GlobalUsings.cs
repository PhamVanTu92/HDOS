extern alias SdkAlias;

global using Xunit;
global using Microsoft.Extensions.Logging.Abstractions;
global using System.Net;
global using System.Net.Http;
global using System.Diagnostics;
global using System.Threading.Channels;
global using Grpc.Core;
global using Microsoft.Extensions.DependencyInjection;

// Proto types from the test project's own compilation (GrpcServices="Server")
global using ReportingPlatform.Provider.V1;

// Disambiguate Status: always use the proto Status enum, not Grpc.Core.Status
global using ProtoStatus = ReportingPlatform.Provider.V1.Status;

// SDK public types
global using SdkAlias::ReportingPlatform.ProviderSdk;

// SDK internal types (visible via InternalsVisibleTo)
global using SdkAlias::ReportingPlatform.ProviderSdk.Internal;

// Helpers
global using ReportingPlatform.ProviderSdk.Tests.Helpers;
