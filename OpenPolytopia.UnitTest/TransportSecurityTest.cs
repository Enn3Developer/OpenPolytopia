namespace OpenPolytopia;

using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Common.Network;
using Common.Network.Packets;
using Server;
using Shouldly;

/// <summary>
/// Covers the transport of the connection: plaintext on loopback, TLS everywhere else
/// </summary>
public class TransportSecurityTest {
  private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

  private const string CERTIFICATE_PASSWORD = "test-password";

  [Fact]
  public void TestTlsIsOffForLoopbackAndOnForEverythingElse() {
    new ClientConnection("127.0.0.1", 1).UsesTls.ShouldBeFalse();
    new ClientConnection("localhost", 1).UsesTls.ShouldBeFalse();
    new ClientConnection("LOCALHOST", 1).UsesTls.ShouldBeFalse();
    new ClientConnection("::1", 1).UsesTls.ShouldBeFalse();

    new ClientConnection("example.com", 1).UsesTls.ShouldBeTrue();
    new ClientConnection("203.0.113.7", 1).UsesTls.ShouldBeTrue();

    // an explicit choice always wins over the default
    new ClientConnection("127.0.0.1", 1, true).UsesTls.ShouldBeTrue();
    new ClientConnection("example.com", 1, false).UsesTls.ShouldBeFalse();
  }

  [Fact]
  public async Task TestPlaintextLoopbackConnectionDeliversPackets() {
    var port = FreePort();
    using var server = new ServerConnection(port, "127.0.0.1");
    server.TlsEnabled.ShouldBeFalse();

    var received = new TaskCompletionSource<IPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
    server.OnPacketReceived += (_, packet) => {
      received.TrySetResult(packet);
      return Task.CompletedTask;
    };

    _ = server.RunAsync();
    await WaitForListenerAsync(port);

    using var client = new ClientConnection("127.0.0.1", port);
    await client.ConnectAsync();
    await client.SendPacketAsync(new SetNamePacket { Name = "plaintext" });

    var packet = await received.Task.WaitAsync(_timeout);
    packet.ShouldBeOfType<SetNamePacket>().Name.ShouldBe("plaintext");
  }

  [Fact]
  public async Task TestTlsConnectionRejectsAnUntrustedCertificate() {
    var port = FreePort();
    using var certificate = CreateSelfSignedCertificate();
    using var server = new ServerConnection(port, "127.0.0.1", certificate);
    server.TlsEnabled.ShouldBeTrue();

    _ = server.RunAsync();
    await WaitForListenerAsync(port);

    // the certificate is self signed, so no trust store on earth accepts it:
    // the client must refuse instead of falling back to plaintext
    using var client = new ClientConnection("localhost", port, true);
    await Should.ThrowAsync<AuthenticationException>(client.ConnectAsync().WaitAsync(_timeout));
    client.Connected.ShouldBeFalse();
  }

  [Fact]
  public async Task TestTlsServerDropsPlaintextClientsWithoutBlockingTheOthers() {
    var port = FreePort();
    using var certificate = CreateSelfSignedCertificate();
    using var server = new ServerConnection(port, "127.0.0.1", certificate);

    var connected = 0;
    server.OnClientConnected += _ => Interlocked.Increment(ref connected);

    _ = server.RunAsync();
    await WaitForListenerAsync(port);

    // a client speaking plaintext to a TLS server never becomes a connection
    using var plaintext = new ClientConnection("127.0.0.1", port, false);
    await plaintext.ConnectAsync();
    await plaintext.SendPacketAsync(new SetNamePacket { Name = "clear" });

    // the accept loop keeps working while that socket is being dropped
    await WaitForListenerAsync(port);

    connected.ShouldBe(0);
  }

  [Fact]
  public async Task TestPacketsSurviveTheRoundTripOverAnAuthenticatedStream() {
    PacketRegistrar.RegisterAllPackets();

    using var certificate = CreateSelfSignedCertificate();
    using var listener = new TcpListenerHolder();
    var acceptTask = listener.Listener.AcceptTcpClientAsync();

    using var clientTcp = new TcpClient();
    await clientTcp.ConnectAsync(IPAddress.Loopback, listener.Port);
    using var serverTcp = await acceptTask.WaitAsync(_timeout);

    await using var serverSsl = new SslStream(serverTcp.GetStream(), false);

    // the test pins this exact certificate instead of weakening the validation:
    // production code never gets a callback at all
    await using var clientSsl = new SslStream(clientTcp.GetStream(), false,
      (_, remote, _, _) => remote != null && remote.GetCertHashString() == certificate.GetCertHashString());

    await Task.WhenAll(
      serverSsl.AuthenticateAsServerAsync(certificate),
      clientSsl.AuthenticateAsClientAsync("localhost")).WaitAsync(_timeout);

    clientSsl.IsEncrypted.ShouldBeTrue();

    var received = new TaskCompletionSource<IPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var serverConnection = new NetworkConnection(1, serverTcp, serverSsl);
    serverConnection.OnPacketReceived += (_, packet) => {
      received.TrySetResult(packet);
      return Task.CompletedTask;
    };
    _ = serverConnection.RunAsync();

    using var clientConnection = new NetworkConnection(0, clientTcp, clientSsl);
    await clientConnection.SendPacketAsync(new SetNamePacket { Name = "encrypted" });

    var packet = await received.Task.WaitAsync(_timeout);
    packet.ShouldBeOfType<SetNamePacket>().Name.ShouldBe("encrypted");
  }

  [Fact]
  public void TestPlaintextIsOnlyAllowedOnLoopback() {
    // every interface, so reachable from the network
    Should.Throw<InvalidOperationException>(() => ServerTls.Validate(null, null));
    Should.Throw<InvalidOperationException>(() => ServerTls.Validate("0.0.0.0", null));
    Should.Throw<InvalidOperationException>(() => ServerTls.Validate("203.0.113.7", null));

    // not an ip address, so we can't prove it's loopback
    Should.Throw<InvalidOperationException>(() => ServerTls.Validate("example.com", null));

    Should.NotThrow(() => ServerTls.Validate("127.0.0.1", null));
    Should.NotThrow(() => ServerTls.Validate("::1", null));
  }

  [Fact]
  public void TestACertificateAllowsAnyBindAddress() {
    using var certificate = CreateSelfSignedCertificate();

    Should.NotThrow(() => ServerTls.Validate(null, certificate));
    Should.NotThrow(() => ServerTls.Validate("0.0.0.0", certificate));
  }

  [Fact]
  public void TestTheCertificateGetsLoadedFromTheEnvironment() {
    var path = Path.Combine(Path.GetTempPath(), $"openpolytopia-tls-{Guid.NewGuid():N}.pfx");
    using var source = CreateSelfSignedCertificate();
    File.WriteAllBytes(path, source.Export(X509ContentType.Pfx, CERTIFICATE_PASSWORD));

    var oldPath = Environment.GetEnvironmentVariable(ServerTls.CERTIFICATE_PATH_ENV);
    var oldPassword = Environment.GetEnvironmentVariable(ServerTls.CERTIFICATE_PASSWORD_ENV);

    try {
      Environment.SetEnvironmentVariable(ServerTls.CERTIFICATE_PATH_ENV, null);
      ServerTls.LoadCertificate().ShouldBeNull();

      Environment.SetEnvironmentVariable(ServerTls.CERTIFICATE_PATH_ENV, path);
      Environment.SetEnvironmentVariable(ServerTls.CERTIFICATE_PASSWORD_ENV, CERTIFICATE_PASSWORD);

      using var loaded = ServerTls.LoadCertificate();
      loaded.ShouldNotBeNull();
      loaded.Subject.ShouldBe(source.Subject);
      loaded.HasPrivateKey.ShouldBeTrue();

      // a wrong password must fail loudly at startup instead of silently running plaintext
      Environment.SetEnvironmentVariable(ServerTls.CERTIFICATE_PASSWORD_ENV, "wrong");
      Should.Throw<InvalidOperationException>(() => ServerTls.LoadCertificate());

      Environment.SetEnvironmentVariable(ServerTls.CERTIFICATE_PATH_ENV, $"{path}.missing");
      Should.Throw<FileNotFoundException>(() => ServerTls.LoadCertificate());
    }
    finally {
      Environment.SetEnvironmentVariable(ServerTls.CERTIFICATE_PATH_ENV, oldPath);
      Environment.SetEnvironmentVariable(ServerTls.CERTIFICATE_PASSWORD_ENV, oldPassword);
      File.Delete(path);
    }
  }

  /// <summary>
  /// Creates a self signed certificate for localhost, only good enough for a test
  /// </summary>
  private static X509Certificate2 CreateSelfSignedCertificate() {
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    var alternativeNames = new SubjectAlternativeNameBuilder();
    alternativeNames.AddDnsName("localhost");
    alternativeNames.AddIpAddress(IPAddress.Loopback);
    request.CertificateExtensions.Add(alternativeNames.Build());
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));

    // server authentication
    request.CertificateExtensions.Add(
      new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));

    using var certificate =
      request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

    // round trip through a pfx so the private key is usable by SslStream on every platform
    return X509CertificateLoader.LoadPkcs12(
      certificate.Export(X509ContentType.Pfx, CERTIFICATE_PASSWORD), CERTIFICATE_PASSWORD,
      X509KeyStorageFlags.Exportable);
  }

  /// <summary>
  /// Grabs a free loopback port
  /// </summary>
  private static int FreePort() {
    using var holder = new TcpListenerHolder();
    return holder.Port;
  }

  /// <summary>
  /// Waits until something is accepting connections on the port
  /// </summary>
  private static async Task WaitForListenerAsync(int port) {
    var deadline = DateTime.UtcNow + _timeout;

    while (DateTime.UtcNow < deadline) {
      try {
        using var probe = new TcpClient();
        await probe.ConnectAsync(IPAddress.Loopback, port);
        return;
      }
      catch (SocketException) {
        await Task.Delay(20);
      }
    }

    throw new TimeoutException($"Nothing started listening on port {port}");
  }

  /// <summary>
  /// A started loopback listener on an OS assigned port
  /// </summary>
  private sealed class TcpListenerHolder : IDisposable {
    public TcpListener Listener { get; }
    public int Port { get; }

    public TcpListenerHolder() {
      Listener = new TcpListener(IPAddress.Loopback, 0);
      Listener.Start();
      Port = ((IPEndPoint)Listener.LocalEndpoint).Port;
    }

    public void Dispose() => Listener.Dispose();
  }
}
