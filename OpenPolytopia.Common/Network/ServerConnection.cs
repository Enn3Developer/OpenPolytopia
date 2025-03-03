namespace OpenPolytopia.Common.Network;

using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using Packets;

public class ServerConnection(int port) : NetworkConnection, IDisposable {
  private readonly TcpListener _tcpListener = TcpListener.Create(port);
  private readonly ConcurrentStack<Task> _clientTasks = [];
  private readonly CancellationTokenSource _cts = new();
  private readonly ConcurrentDictionary<uint, CancellationTokenSource> _timerCancellationTokens = new();


  /// <summary>
  /// Starts the server
  /// </summary>
  public void Start() {
    RegisterCustomStreamHandler(SendKeepAliveAsync);
    _tcpListener.Start();
  }

  /// <summary>
  /// Stops the server
  /// </summary>
  public void Stop() {
    // Stop the listener, we won't accept incoming requests
    _tcpListener.Stop();
    // Stop all the running tasks
    _cts.Cancel();

    // Wait for all tasks to complete
    foreach (var task in _clientTasks) {
      task.Wait();
    }

    // Dispose of the tasks
    RemoveCompletedTasks();
  }

  /// <summary>
  /// Listens to incoming connections
  /// </summary>
  /// <remarks>
  /// Remember to call <see cref="ServerConnection.Start"/> before listening to connections
  /// </remarks>
  public async Task ListenAsync() {
    var client = await _tcpListener.AcceptTcpClientAsync();
    var task = ManageClientAsync(client, _cts.Token);
    task.Start();
    _clientTasks.Push(task);
  }

  /// <summary>
  /// Removes all the tasks where the connection was closed
  /// </summary>
  public void Update() => RemoveCompletedTasks();

  private void RemoveCompletedTasks() {
    // TODO: implement cleaning of completed tasks
  }

  protected override async Task ManageKeepAlivePacketAsync(KeepAlivePacket packet, NetworkStream stream,
    List<byte> bytes) {
    if (_timerCancellationTokens.TryGetValue(packet.Captcha, out var cts)) {
      await cts.CancelAsync();
    }
  }

  private async Task SendKeepAliveAsync(NetworkStream stream, CancellationTokenSource cts) {
    try {
      var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
      List<byte> bytes = [];
      var ct = new CancellationTokenSource();
      while (!cts.IsCancellationRequested) {
        await timer.WaitForNextTickAsync(cts.Token);
        var packet = new KeepAlivePacket { Captcha = (uint)RandomNumberGenerator.GetInt32(short.MaxValue) };
        var captcha = packet.Captcha;
        await stream.WritePacketAsync(packet, bytes);

        if (ct.TryReset()) {
          _timerCancellationTokens[captcha] = ct;
        }
        else {
          ct = new CancellationTokenSource();
          _timerCancellationTokens[captcha] = ct;
        }

        try {
          await timer.WaitForNextTickAsync(ct.Token);
          if (!cts.IsCancellationRequested) {
            await cts.CancelAsync();
          }
        }
        catch (OperationCanceledException) { }
      }
    }
    catch (OperationCanceledException) {
    }
  }

  ~ServerConnection() {
    Dispose();
  }

  public void Dispose() {
    _tcpListener.Dispose();
    _cts.Dispose();
  }
}
