#!/usr/bin/env python3
"""latency_proxy.py - a local TCP proxy that delays what the client sends, to approximate a remote host.

bench.py --latency starts it in front of the dev site (tests/hands-on/README.md, "Benchmarks"). It forwards
raw TCP, so TLS passes through untouched and the site's localhost certificate still matches. Each chunk the
client sends reaches the site `--delay` ms after it arrived, and replies come back at once, so every request
round trip gains about that delay. A new connection pays it too (about twice: the TCP and TLS handshakes).
Chunks are delayed in flight, not one after another, so a request split into several chunks gains the delay
once, as on a real link. Bandwidth, packet loss and jitter are not simulated.

  python3 tools/latency_proxy.py --target-port 44800 --delay 25

It listens on both 127.0.0.1 and ::1 (on Windows a client that tries ::1 first and finds nothing there waits
about 2 s per connection), prints `listening <port>` once it accepts connections, and runs until killed.
"""
import argparse
import asyncio
import socket
import sys
import time

CHUNK = 65536
LOOPBACKS = ("127.0.0.1", "::1")


def free_port():
    """A TCP port that is free on both loopback addresses.

    :returns: the port number.
    :raises OSError: when no port is free on both after a few tries.
    """
    for _ in range(20):
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as v4:
            v4.bind(("127.0.0.1", 0))
            port = v4.getsockname()[1]
            try:
                with socket.socket(socket.AF_INET6, socket.SOCK_STREAM) as v6:
                    v6.bind(("::1", port))
            except OSError:
                continue  # taken on ::1: try another
            return port
    raise OSError("no TCP port is free on both 127.0.0.1 and ::1")


async def delayed_pipe(reader, writer, delay):
    """Copy one direction of a connection, delivering each chunk `delay` seconds after it arrived.

    Reading and writing run as two tasks joined by a queue, so chunks in flight overlap: each one is late by
    the delay, not by the delay times the chunks ahead of it.

    :param reader: where the chunks come from.
    :param writer: where they go.
    :param delay: the one-way delay in seconds; 0 copies straight through.
    """
    queue = asyncio.Queue()

    async def receive():
        try:
            while data := await reader.read(CHUNK):
                await queue.put((time.monotonic() + delay, data))
        except (ConnectionError, OSError):
            pass
        finally:
            await queue.put((time.monotonic() + delay, b""))  # end of stream, delayed like the data before it

    async def deliver():
        try:
            while True:
                due, data = await queue.get()
                if (wait := due - time.monotonic()) > 0:
                    await asyncio.sleep(wait)
                if not data:
                    break
                writer.write(data)
                await writer.drain()
        except (ConnectionError, OSError):
            pass
        finally:
            writer.close()

    await asyncio.gather(receive(), deliver())


async def handle(client_reader, client_writer, target_port, delay):
    """Proxy one client connection to the target, delaying the client-to-server direction.

    :param client_reader: the client's stream reader.
    :param client_writer: the client's stream writer.
    :param target_port: the site's port on 127.0.0.1.
    :param delay: the delay in seconds.
    """
    try:
        server_reader, server_writer = await asyncio.open_connection("127.0.0.1", target_port)
    except OSError:
        client_writer.close()
        return
    await asyncio.gather(delayed_pipe(client_reader, server_writer, delay),
                         delayed_pipe(server_reader, client_writer, 0))


async def serve(port, target_port, delay):
    """Listen on both loopback addresses and proxy every connection until the process is killed.

    :param port: the port to listen on.
    :param target_port: the site's port on 127.0.0.1.
    :param delay: the delay in seconds.
    """
    server = await asyncio.start_server(lambda r, w: handle(r, w, target_port, delay), list(LOOPBACKS), port)
    print(f"listening {port}", flush=True)
    async with server:
        await server.serve_forever()


def main():
    p = argparse.ArgumentParser(prog="latency_proxy.py", description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--target-port", type=int, required=True, help="The site's port on 127.0.0.1")
    p.add_argument("--delay", type=float, required=True, help="Milliseconds added to each chunk the client sends")
    p.add_argument("--port", type=int, help="Port to listen on. Default: a free one, printed on start")
    a = p.parse_args()
    if a.delay < 0:
        p.error("--delay can't be negative")
    try:
        asyncio.run(serve(a.port or free_port(), a.target_port, a.delay / 1000))
    except KeyboardInterrupt:
        sys.exit(0)


if __name__ == "__main__":
    main()
