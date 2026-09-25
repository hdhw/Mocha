using Mocha2023.Auth;
using Mocha2023.Classes.DBs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Mocha2023.Classes
{
    // Blocks a banned account's RecNet API calls server-side. The client's
    // ban-gate screen (app.js's renderBanGate()) is what a legitimate browser
    // sees, but that's just UX - nothing stops a banned player from skipping
    // the SPA and hitting these endpoints directly (curl, devtools, a
    // modified client), so "banned players can't use RecNet" has to be
    // enforced here too, not only in the front end.
    //
    // A handful of routes stay open for a banned account on purpose - they're
    // what lets the gate screen itself work (auth/me, so the client can even
    // learn it's banned; auth/logout, so they can sign out) and the whole
    // ban-appeal flow (banappeal/...), which obviously has to stay reachable
    // to a banned account or nobody could ever appeal.
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BanGateAttribute : Attribute, IAsyncActionFilter
    {
        private static readonly string[] AllowedWhileBanned =
        {
            "/auth/me",
            "/auth/logout",
            "/banappeal/"
        };

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            string path = context.HttpContext.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            bool exempt = AllowedWhileBanned.Any(allowed =>
                path.Contains(allowed, StringComparison.Ordinal));

            if (!exempt)
            {
                long? accountId = AuthStuff.GetPlayerId(context.HttpContext.Request);
                if (accountId != null && PlayerDB.IsPlayerBanned(accountId.Value, out var details))
                {
                    context.Result = new ObjectResult(new
                    {
                        error = "This account is banned.",
                        banned = true,
                        reason = details!.Message,
                        isPermanent = details.Duration <= 0,
                        bannedAtUnix = details.ModerationSetUnixTime
                    })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return;
                }
            }

            await next();
        }
    }
}
