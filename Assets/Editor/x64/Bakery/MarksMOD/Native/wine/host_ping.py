#!/usr/bin/env python3
import socket
import struct
import sys
import time

HOST = "127.0.0.1"
PORT = 27778
MAGIC = 0x42414B31
CMD_PING = 1


def port_open(timeout=0.5):
    try:
        with socket.create_connection((HOST, PORT), timeout=timeout):
            return True
    except OSError:
        return False


def ping():
    with socket.create_connection((HOST, PORT), timeout=5) as sock:
        sock.sendall(struct.pack("<III", MAGIC, CMD_PING, 0))
        data = sock.recv(4)
        if len(data) != 4:
            return 2
        status = struct.unpack("<i", data)[0]
        print("ping status:", status)
        return 0 if status == 1 else 3


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--wait":
        for _ in range(40):
            if port_open():
                sys.exit(0)
            time.sleep(0.5)
        sys.exit(1)
    sys.exit(ping())
