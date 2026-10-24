#!/usr/bin/env python3
import socket
import struct
import sys
import time

HOST = "127.0.0.1"
PORT = 27778
MAGIC = 0x42414B31
CMD_PING = 1
CMD_SHUTDOWN = 7


def send_command(cmd, timeout=2.0):
    try:
        with socket.create_connection((HOST, PORT), timeout=timeout) as sock:
            sock.settimeout(timeout)
            sock.sendall(struct.pack("<III", MAGIC, cmd, 0))
            data = sock.recv(4)
            if len(data) != 4:
                return None
            return struct.unpack("<i", data)[0]
    except OSError:
        return None


def ping():
    status = send_command(CMD_PING)
    if status is None:
        return 2
    print("ping status:", status)
    return 0 if status == 1 else 3


def stop():
    status = send_command(CMD_SHUTDOWN)
    if status is None:
        return 2
    print("shutdown status:", status)
    return 0 if status == 0 else 3


def wait(timeout=120.0):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if send_command(CMD_PING, 0.5) == 1:
            return 0
        time.sleep(0.25)
    return 1


if __name__ == "__main__":
    action = sys.argv[1] if len(sys.argv) > 1 else None
    if action == "--wait":
        timeout = float(sys.argv[2]) if len(sys.argv) > 2 else 120.0
        sys.exit(wait(timeout))
    if action == "--stop":
        sys.exit(stop())
    sys.exit(ping())
