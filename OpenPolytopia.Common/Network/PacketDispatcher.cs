namespace OpenPolytopia.Common.Network;

using Packets;

/// <summary>
/// Routes a packet to the handler registered for its type
/// </summary>
/// <remarks>
/// Handlers are looked up by the exact runtime type of the packet, so a handler registered
/// for a base type never runs for a derived one.
/// Register every handler before the connection starts dispatching:
/// registration isn't thread safe, dispatching is
/// </remarks>
/// <typeparam name="TContext">what gets handed to the handlers along with the packet,
/// e.g. the connection the packet came from</typeparam>
public class PacketDispatcher<TContext> {
  private readonly Dictionary<Type, Func<TContext, IPacket, Task>> _handlers = new(32);

  /// <summary>
  /// Registers an asynchronous handler for a packet type
  /// </summary>
  /// <param name="handler">the handler to run when a packet of this type arrives</param>
  /// <typeparam name="T">the type of the packet</typeparam>
  /// <exception cref="ArgumentException">if a handler is already registered for this type</exception>
  public void Register<T>(Func<TContext, T, Task> handler) where T : IPacket =>
    _handlers.Add(typeof(T), (context, packet) => handler(context, (T)packet));

  /// <summary>
  /// Registers a synchronous handler for a packet type
  /// </summary>
  /// <param name="handler">the handler to run when a packet of this type arrives</param>
  /// <typeparam name="T">the type of the packet</typeparam>
  /// <exception cref="ArgumentException">if a handler is already registered for this type</exception>
  public void Register<T>(Action<TContext, T> handler) where T : IPacket =>
    _handlers.Add(typeof(T), (context, packet) => {
      handler(context, (T)packet);
      return Task.CompletedTask;
    });

  /// <summary>
  /// Runs the handler registered for the packet
  /// </summary>
  /// <param name="context">what to hand to the handler along with the packet</param>
  /// <param name="packet">the packet to dispatch</param>
  /// <returns><c>true</c> if a handler ran, <c>false</c> if no handler is registered for this packet type</returns>
  public async Task<bool> DispatchAsync(TContext context, IPacket packet) {
    if (!_handlers.TryGetValue(packet.GetType(), out var handler)) {
      return false;
    }

    await handler(context, packet);
    return true;
  }
}
