namespace OpenPolytopia.Server;

using System.Net;
using System.Security.Cryptography.X509Certificates;

/// <summary>
/// Loads and validates the TLS configuration of the server
/// </summary>
/// <remarks>
/// Passwords and tokens travel on this connection, so a server reachable from outside this machine
/// has to serve TLS; the certificate comes from a PKCS#12 file pointed at by
/// <see cref="CERTIFICATE_PATH_ENV"/>, unlocked with <see cref="CERTIFICATE_PASSWORD_ENV"/>
/// </remarks>
public static class ServerTls {
  /// <summary>
  /// Environment variable holding the path of the PKCS#12 (.pfx) file with the certificate and its private key
  /// </summary>
  public const string CERTIFICATE_PATH_ENV = "OPENPOLYTOPIA_TLS_CERTIFICATE";

  /// <summary>
  /// Environment variable holding the password of the PKCS#12 file; unset means no password
  /// </summary>
  public const string CERTIFICATE_PASSWORD_ENV = "OPENPOLYTOPIA_TLS_PASSWORD";

  /// <summary>
  /// Loads the certificate configured through the environment
  /// </summary>
  /// <returns>the certificate or null if <see cref="CERTIFICATE_PATH_ENV"/> isn't set</returns>
  /// <exception cref="FileNotFoundException">if the configured file doesn't exist</exception>
  /// <exception cref="InvalidOperationException">if the file can't be read as a certificate with a private key</exception>
  public static X509Certificate2? LoadCertificate() {
    var path = Environment.GetEnvironmentVariable(CERTIFICATE_PATH_ENV);
    if (string.IsNullOrWhiteSpace(path)) {
      return null;
    }

    if (!File.Exists(path)) {
      throw new FileNotFoundException($"{CERTIFICATE_PATH_ENV} points to a file that doesn't exist", path);
    }

    var password = Environment.GetEnvironmentVariable(CERTIFICATE_PASSWORD_ENV);

    X509Certificate2 certificate;
    try {
      certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password);
    }
    catch (Exception e) {
      throw new InvalidOperationException($"Failed to load the TLS certificate from '{path}': {e.Message}", e);
    }

    // without the private key we can't answer a single handshake, better to fail at startup
    if (!certificate.HasPrivateKey) {
      certificate.Dispose();
      throw new InvalidOperationException($"The TLS certificate at '{path}' has no private key");
    }

    return certificate;
  }

  /// <summary>
  /// Checks that the given bind address is allowed to run without TLS
  /// </summary>
  /// <remarks>
  /// Only a server bound to loopback, so tests and local development, may run plaintext;
  /// anything reachable from the network needs a certificate
  /// </remarks>
  /// <param name="bindAddress">the address the server binds to; null means every interface</param>
  /// <param name="certificate">the loaded certificate, or null if there is none</param>
  /// <exception cref="InvalidOperationException">if a non loopback server has no certificate</exception>
  public static void Validate(string? bindAddress, X509Certificate2? certificate) {
    if (certificate != null || IsLoopback(bindAddress)) {
      return;
    }

    throw new InvalidOperationException(
      $"Refusing to listen on '{bindAddress ?? "*"}' without TLS: accounts send passwords and tokens over " +
      $"this connection. Set {CERTIFICATE_PATH_ENV} to a .pfx file (and {CERTIFICATE_PASSWORD_ENV} to its " +
      "password) or bind to 127.0.0.1 for local development.");
  }

  /// <summary>
  /// Loads the configured certificate and checks it against the bind address
  /// </summary>
  /// <param name="bindAddress">the address the server binds to; null means every interface</param>
  /// <returns>the certificate to serve TLS with, or null when running plaintext on loopback</returns>
  /// <exception cref="InvalidOperationException">if a non loopback server has no certificate</exception>
  public static X509Certificate2? LoadAndValidate(string? bindAddress) {
    var certificate = LoadCertificate();

    try {
      Validate(bindAddress, certificate);
    }
    catch {
      certificate?.Dispose();
      throw;
    }

    return certificate;
  }

  /// <summary>
  /// Checks if a bind address only accepts connections from this same machine
  /// </summary>
  /// <param name="bindAddress">the address the server binds to; null means every interface</param>
  private static bool IsLoopback(string? bindAddress) =>
    !string.IsNullOrWhiteSpace(bindAddress) &&
    IPAddress.TryParse(bindAddress, out var ip) &&
    IPAddress.IsLoopback(ip);
}
