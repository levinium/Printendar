using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Printendar.Core.Sources;

namespace Printendar.Sources.Microsoft365;

/// <summary>
/// Signs in to Microsoft 365 and keeps the token so it only has to happen once.
/// </summary>
/// <remarks>
/// Silent first, interactive only when that fails. Someone who signed in last week should not
/// be asked again every time the program starts; being asked to sign in repeatedly is exactly
/// the friction this program is meant to avoid.
///
/// The browser flow is the system browser on a loopback redirect, not an embedded web view.
/// It is the only flow that works the same on Windows, macOS and Linux, it lets the user see
/// the real Microsoft address bar before typing a password, and it picks up any session they
/// already have.
/// </remarks>
public sealed class Microsoft365Authenticator : IAsyncDisposable
{
    private readonly Microsoft365Options _options;
    private readonly IPublicClientApplication _application;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MsalCacheHelper? _cacheHelper;
    private bool _cacheAttached;

    public Microsoft365Authenticator(Microsoft365Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Printendar has no Microsoft application id, so it cannot sign in. Set " +
                $"{Microsoft365Options.ClientIdEnvironmentVariable} to the client id of an Entra " +
                "application registration, or use a build that has one.");
        }

        _options = options;

        _application = PublicClientApplicationBuilder
            .Create(options.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, options.Tenant)
            // Loopback redirect on a free port. Registered as a "Mobile and desktop" platform
            // with http://localhost, which the identity platform allows for public clients.
            .WithDefaultRedirectUri()
            .Build();
    }

    /// <summary>Whether a token can probably be had without asking the user.</summary>
    public async ValueTask<AuthState> GetStateAsync(CancellationToken cancellationToken)
    {
        var account = await FirstAccountAsync().ConfigureAwait(false);

        if (account is null)
        {
            return AuthState.NotConnected;
        }

        try
        {
            await AcquireSilentAsync(account, cancellationToken).ConfigureAwait(false);
            return AuthState.Connected;
        }
        catch (MsalUiRequiredException)
        {
            return AuthState.NeedsInteraction;
        }
    }

    /// <summary>
    /// Returns a usable access token, asking the user only if it cannot be avoided.
    /// </summary>
    public async Task<AuthenticationResult> AcquireTokenAsync(
        bool allowInteraction,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureCacheAsync().ConfigureAwait(false);

            var account = await FirstAccountAsync().ConfigureAwait(false);

            if (account is not null)
            {
                try
                {
                    return await AcquireSilentAsync(account, cancellationToken).ConfigureAwait(false);
                }
                catch (MsalUiRequiredException) when (allowInteraction)
                {
                    // Expected: consent changed, a policy requires a fresh sign-in, or the
                    // refresh token aged out. Fall through and ask.
                }
            }

            if (!allowInteraction)
            {
                throw new MsalUiRequiredException("printendar_interaction_required", "Sign-in is required.");
            }

            return await _application
                .AcquireTokenInteractive(_options.Scopes)
                // The system browser, not an embedded web view. Embedded views are blocked by
                // many tenants' conditional access policies and cannot reuse an existing
                // session.
                .WithUseEmbeddedWebView(false)
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await EnsureCacheAsync().ConfigureAwait(false);

        foreach (var account in await _application.GetAccountsAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _application.RemoveAsync(account).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _gate.Dispose();

        if (_cacheHelper is not null)
        {
            _cacheHelper.UnregisterCache(_application.UserTokenCache);
        }

        await ValueTask.CompletedTask;
    }

    private Task<AuthenticationResult> AcquireSilentAsync(IAccount account, CancellationToken cancellationToken) =>
        _application.AcquireTokenSilent(_options.Scopes, account).ExecuteAsync(cancellationToken);

    private async Task<IAccount?> FirstAccountAsync()
    {
        await EnsureCacheAsync().ConfigureAwait(false);

        return (await _application.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
    }

    /// <summary>
    /// Attaches the on-disk token cache so a sign-in survives restarting the program.
    /// </summary>
    /// <remarks>
    /// Encrypted by the platform: DPAPI on Windows, Keychain on macOS, and libsecret on Linux.
    /// A Linux desktop without a secret service has no keyring to use, and in that case the
    /// cache is left in memory rather than written unprotected to disk. The cost is signing in
    /// again next launch; writing a refresh token in the clear would be a worse trade.
    /// </remarks>
    private async Task EnsureCacheAsync()
    {
        if (_cacheAttached)
        {
            return;
        }

        _cacheAttached = true;

        try
        {
            var cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Printendar");

            Directory.CreateDirectory(cacheDirectory);

            var properties = new StorageCreationPropertiesBuilder("msal.cache", cacheDirectory)
                .WithMacKeyChain("Printendar", "MSALCache")
                .WithLinuxKeyring(
                    schemaName: "org.printendar.tokencache",
                    collection: "default",
                    secretLabel: "Printendar token cache",
                    attribute1: new KeyValuePair<string, string>("Product", "Printendar"),
                    attribute2: new KeyValuePair<string, string>("Component", "TokenCache"))
                .Build();

            _cacheHelper = await MsalCacheHelper.CreateAsync(properties).ConfigureAwait(false);
            _cacheHelper.VerifyPersistence();
            _cacheHelper.RegisterCache(_application.UserTokenCache);
        }
        catch (MsalCachePersistenceException)
        {
            // No usable keyring, which is normal on a bare Linux desktop. Carry on with an
            // in-memory cache rather than writing a refresh token unprotected.
            _cacheHelper = null;
        }
    }
}
