namespace Ignixa.Lab.Functions.Middleware;

/// <summary>
/// Rate-limiting tier a request belongs to, derived from its function name.
/// </summary>
public enum EndpointClass
{
    /// <summary>No limiting (health probes, CORS preflight).</summary>
    Exempt,

    /// <summary>Cheap in-memory catalog read (<c>GET /api/suites</c>).</summary>
    Suites,

    /// <summary>Single outbound metadata call (<c>GET /api/capability</c>).</summary>
    Capability,

    /// <summary>Expensive fan-out run (<c>POST /api/run</c>) — the primary abuse vector.</summary>
    Run,
}
