namespace Printendar.Sources.Microsoft365;

/// <summary>
/// A sign-in failure that already knows what the user should do about it.
/// </summary>
/// <remarks>
/// Carries a <see cref="SignInDiagnosis"/> so the window can show advice and, where it helps,
/// an administrator approval link, without knowing anything about the identity platform.
/// </remarks>
public sealed class Microsoft365SignInException(SignInDiagnosis diagnosis, Exception inner)
    : Exception(diagnosis.Message, inner)
{
    public SignInDiagnosis Diagnosis { get; } = diagnosis;
}
