// MassTransit 8 registers its own IHealthCheck automatically when AddMassTransit() is called.
// Health check name and tags are configured via x.ConfigureHealthCheckOptions() inside
// the AddMassTransit() lambda in Program.cs — no custom class needed.
//
// The registered check is named "rabbitmq" with tag "ready" and reports:
//   Healthy  — all receive endpoints bound and consuming
//   Degraded — endpoints bound but some reconnecting
//   Unhealthy — bus not connected

// This file is intentionally empty. It exists as a placeholder so the project structure
// matches the plan, and to document where a custom health check WOULD live if needed
// (e.g., if switching from MassTransit's built-in to a manual TCP probe in Phase 12).
