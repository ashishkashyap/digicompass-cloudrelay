using System;

namespace DigiCompassCloudRelay;

public static class RelayConfig
{
    public static string TablesConnection =>
        Environment.GetEnvironmentVariable("TABLES_CONNECTION_STRING")
        ?? throw new Exception("TABLES_CONNECTION_STRING missing");

    public static string ResendApiKey =>
        Environment.GetEnvironmentVariable("RESEND_API_KEY")
        ?? throw new Exception("RESEND_API_KEY missing");

    public static string ResendFrom =>
        Environment.GetEnvironmentVariable("RESEND_FROM")
        ?? "DigiKids <onboarding@resend.dev>";

    public static string RelayApiKey =>
        Environment.GetEnvironmentVariable("RELAY_API_KEY")
        ?? throw new Exception("RELAY_API_KEY missing");
}