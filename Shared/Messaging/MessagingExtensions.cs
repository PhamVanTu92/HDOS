using Microsoft.Extensions.Configuration;

namespace ReportingPlatform.Messaging;

public static class MessagingExtensions
{
    // Registers MassTransit with RabbitMQ transport. Consumers are registered by the
    // calling service via the configure delegate; this method only sets up the transport.
    public static IServiceCollection AddPlatformMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator> configure)
    {
        services.AddMassTransit(bus =>
        {
            configure(bus);

            bus.UsingRabbitMq((ctx, rmq) =>
            {
                rmq.Host(
                    configuration["Messaging:Host"] ?? "localhost",
                    configuration["Messaging:VirtualHost"] ?? "/",
                    h =>
                    {
                        h.Username(configuration["Messaging:Username"] ?? "guest");
                        h.Password(configuration["Messaging:Password"] ?? "guest");
                    });

                rmq.ConfigureEndpoints(ctx);
            });
        });

        return services;
    }
}
