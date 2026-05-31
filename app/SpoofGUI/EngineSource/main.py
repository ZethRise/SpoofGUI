import asyncio
import logging
import os
import socket
import sys
import traceback
import threading
import json

# from utils.proxy_protocols import parse_vless_protocol
from utils.network_tools import get_default_interface_ipv4
from utils.packet_templates import ClientHelloMaker
from fake_tcp import FakeInjectiveConnection, FakeTcpInjector

# SpoofGUI fork patch: the banner below prints non-ASCII (Persian) text. When the
# frozen engine runs with a real stdout pipe, the PyInstaller runtime defaults to
# the OS code page (cp1252) and the print() raises UnicodeEncodeError before the
# listener ever starts. Force UTF-8 with errors='replace' so it can never crash on
# output. (PYTHONIOENCODING/PYTHONUTF8 are not honored by the frozen runtime.)
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

# SpoofGUI fork patch: on the Windows proactor event loop, tearing down a relay
# whose peer still has a pending overlapped recv makes CancelIoEx fail and asyncio
# logs "Cancelling an overlapped future failed" with a full traceback for *every*
# closed connection (WinError 6/64/121, ConnectionResetError). These are benign
# connection-churn errors, not engine faults, so swallow them to keep the log
# usable; real problems still go through the default handler.
_BENIGN_WINERRORS = {6, 64, 121, 10053, 10054, 10038}


def _quiet_loop_exception_handler(loop, context):
    message = context.get("message", "")
    exception = context.get("exception")
    if "overlapped" in message.lower():
        return
    if isinstance(exception, (ConnectionResetError, ConnectionAbortedError)):
        return
    if isinstance(exception, OSError) and getattr(exception, "winerror", None) in _BENIGN_WINERRORS:
        return
    loop.default_exception_handler(context)


logging.getLogger("asyncio").setLevel(logging.CRITICAL)


def get_exe_dir():
    """Returns the directory where the .exe (or script) is located."""
    if getattr(sys, 'frozen', False):
        # Running as a PyInstaller EXE
        return os.path.dirname(sys.executable)
    else:
        # Running as a normal Python script
        return os.path.dirname(os.path.abspath(__file__))


# Build the path to config.json
config_path = os.path.join(get_exe_dir(), 'config.json')

# Load the config
with open(config_path, 'r') as f:
    config = json.load(f)

LISTEN_HOST = config["LISTEN_HOST"]
LISTEN_PORT = config["LISTEN_PORT"]
FAKE_SNI = config["FAKE_SNI"].encode()
CONNECT_IP = config["CONNECT_IP"]
CONNECT_PORT = config["CONNECT_PORT"]
FAST_MODE = bool(config.get("FAST_MODE", False))
INTERFACE_IPV4 = get_default_interface_ipv4(CONNECT_IP)
DATA_MODE = "tls"
BYPASS_METHOD = "wrong_seq"

##################

fake_injective_connections: dict[tuple, FakeInjectiveConnection] = {}


def _configure_sock(sock: socket.socket):
    # SpoofGUI fork patch (ported from atarevals/SNI-Spoofing): TCP_NODELAY kills
    # Nagle latency; Fast Mode uses small buffers (less queuing, lower latency for
    # games/realtime), otherwise large buffers favour throughput.
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_KEEPALIVE, 1)
    try:
        sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_KEEPIDLE, 11)
        sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_KEEPINTVL, 2)
        sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_KEEPCNT, 3)
    except (AttributeError, OSError):
        pass
    sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    if FAST_MODE:
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 32768)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, 32768)
    else:
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 262144)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, 262144)


async def _relay_pair(sock_a: socket.socket, sock_b: socket.socket):
    # SpoofGUI fork patch (ported from atarevals/SNI-Spoofing): bidirectional relay
    # that shuts both sockets down cleanly instead of cancelling the peer task. This
    # avoids the Windows proactor "Cancelling an overlapped future failed" storm and
    # never terminates the engine on a single dropped connection.
    loop = asyncio.get_running_loop()
    shutdown_initiated = False

    def _shutdown_both():
        nonlocal shutdown_initiated
        if shutdown_initiated:
            return
        shutdown_initiated = True
        for s in (sock_a, sock_b):
            try:
                s.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass

    async def _forward(src, dst):
        try:
            while True:
                data = await loop.sock_recv(src, 65536)
                if not data:
                    break
                await loop.sock_sendall(dst, data)
        except (OSError, ConnectionError, asyncio.CancelledError):
            pass
        finally:
            _shutdown_both()

    await asyncio.gather(
        _forward(sock_a, sock_b),
        _forward(sock_b, sock_a),
        return_exceptions=True,
    )

    for s in (sock_a, sock_b):
        try:
            s.close()
        except OSError:
            pass


async def handle(incoming_sock: socket.socket, incoming_remote_addr):
    try:
        loop = asyncio.get_running_loop()
        # try:
        #     data = await loop.sock_recv(incoming_sock, 65575)
        #     if not data:
        #         raise ValueError("eof")
        # except Exception:
        #     incoming_sock.close()
        #     return
        # try:
        #     version, uuid_bytes, transport_protocol, remote_address_type, remote_address, remote_port, payload_index = parse_vless_protocol(
        #         data)
        # except Exception as e:
        #     print("No Vless Request!, Connection Closed", repr(e), data)
        #     incoming_sock.close()
        #     return
        # if transport_protocol != "tcp":
        #     print("Transport Protocol Error!, Connection Closed", transport_protocol, data)
        #     incoming_sock.close()
        #     return
        # if remote_address_type == "hostname":
        #     print("hostname address not implemented yet!", data)
        #     incoming_sock.close()
        #     return
        # if remote_address_type == "ipv4":
        #     if not INTERFACE_IPV4:
        #         print("no interface ipv4!", data)
        #         incoming_sock.close()
        #         return
        #     family = socket.AF_INET
        #     src_ip = INTERFACE_IPV4
        #
        # elif remote_address_type == "ipv6":
        #     if not INTERFACE_IPV6:
        #         print("no interface ipv6!", data)
        #         incoming_sock.close()
        #         return
        #     family = socket.AF_INET6
        #     src_ip = INTERFACE_IPV6
        #
        # else:
        #     print(data)
        #     sys.exit("impossible address type!")

        # try:
        #     fake_sni_host, data_mode, bypass_method = UUID_FAKE_MAP[uuid_bytes]
        # except KeyError:
        #     print("unmatched uuid", uuid_bytes)
        #     incoming_sock.close()
        #     return

        # if data_mode == "http":
        #     ...
        if DATA_MODE == "tls":
            fake_data = ClientHelloMaker.get_client_hello_with(os.urandom(32), os.urandom(32), FAKE_SNI,
                                                               os.urandom(32))
        else:
            sys.exit("impossible mode!")
        outgoing_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        outgoing_sock.setblocking(False)
        outgoing_sock.bind((INTERFACE_IPV4, 0))
        _configure_sock(outgoing_sock)
        src_port = outgoing_sock.getsockname()[1]
        fake_injective_conn = FakeInjectiveConnection(outgoing_sock, INTERFACE_IPV4, CONNECT_IP, src_port, CONNECT_PORT,
                                                      fake_data,
                                                      BYPASS_METHOD, incoming_sock)
        fake_injective_connections[fake_injective_conn.id] = fake_injective_conn
        try:
            await loop.sock_connect(outgoing_sock, (CONNECT_IP, CONNECT_PORT))
        except Exception:
            fake_injective_conn.monitor = False
            del fake_injective_connections[fake_injective_conn.id]
            outgoing_sock.close()
            incoming_sock.close()
            return

        # if bypass_method == "wrong_checksum":
        #     ...

        if BYPASS_METHOD == "wrong_seq":
            try:
                await asyncio.wait_for(fake_injective_conn.t2a_event.wait(), 2)
                if fake_injective_conn.t2a_msg == "unexpected_close":
                    raise ValueError("unexpected close")
                if fake_injective_conn.t2a_msg == "fake_data_ack_recv":
                    pass
                else:
                    sys.exit("impossible t2a msg!")
            except Exception:
                fake_injective_conn.monitor = False
                del fake_injective_connections[fake_injective_conn.id]
                outgoing_sock.close()
                incoming_sock.close()
                return
        else:
            sys.exit("unknown bypass method!")

        fake_injective_conn.monitor = False
        del fake_injective_connections[fake_injective_conn.id]

        # early_data = data[payload_index:]
        # if early_data:
        #     try:
        #         sent_len = await loop.sock_sendall(outgoing_sock, early_data)
        #         if sent_len != len(early_data):
        #             raise ValueError("incomplete send")
        #     except Exception:
        #         outgoing_sock.close()
        #         incoming_sock.close()
        #         return

        await _relay_pair(incoming_sock, outgoing_sock)

    except Exception:
        # SpoofGUI fork patch: a single failed connection must never terminate the
        # engine. Log and return so the listener keeps accepting new clients.
        traceback.print_exc()
        return


async def main():
    mother_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    mother_sock.setblocking(False)
    mother_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    mother_sock.bind((LISTEN_HOST, LISTEN_PORT))
    mother_sock.setsockopt(socket.SOL_SOCKET, socket.SO_KEEPALIVE, 1)
    mother_sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_KEEPIDLE, 11)
    mother_sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_KEEPINTVL, 2)
    mother_sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_KEEPCNT, 3)
    mother_sock.listen()
    loop = asyncio.get_running_loop()
    loop.set_exception_handler(_quiet_loop_exception_handler)
    while True:
        incoming_sock, addr = await loop.sock_accept(mother_sock)
        incoming_sock.setblocking(False)
        _configure_sock(incoming_sock)
        asyncio.create_task(handle(incoming_sock, addr))


if __name__ == "__main__":
    w_filter = "tcp and " + "(" + "(ip.SrcAddr == " + INTERFACE_IPV4 + " and ip.DstAddr == " + CONNECT_IP + ")" + " or " + "(ip.SrcAddr == " + CONNECT_IP + " and ip.DstAddr == " + INTERFACE_IPV4 + ")" + ")"
    fake_tcp_injector = FakeTcpInjector(w_filter, fake_injective_connections)
    threading.Thread(target=fake_tcp_injector.run, args=(), daemon=True).start()
    print("هشن شومافر تیامح دینکیم هدافتسا دازآ تنرتنیا هب یسرتسد یارب همانرب نیا زا رگا")
    print(
        "دراد امش تیامح هب زاین هک مراد رظن رد دازآ تنرتنیا هب ناریا مدرم مامت یسرتسد یارب یدایز یاه همانرب و اه هژورپ")
    print("\n")
    print("USDT (BEP20): 0x76a768B53Ca77B43086946315f0BDF21156bF424\n")
    print("@patterniha")
    asyncio.run(main())
