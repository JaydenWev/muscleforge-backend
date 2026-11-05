#!/usr/bin/env python3
"""Linux PTY regression checks for the CLI's hidden input and cancellation.

Python's standard library provides a real controlling terminal that redirected
.NET subprocess tests cannot supply. Captured terminal data is never printed.
"""

import array
import errno
import fcntl
import os
import pty
import select
import signal
import subprocess
import sys
import termios
import time
import uuid


DETAILS = {}


class ProbeFailure(Exception):
    def __init__(self, tag):
        self.tag = tag


class PtyCommand:
    def __init__(self, arguments):
        self.pid, self.fd = pty.fork()
        if self.pid == 0:
            try:
                os.execvpe(arguments[0], arguments, os.environ.copy())
            except BaseException:
                os._exit(127)
        os.set_blocking(self.fd, False)
        self.output = bytearray()
        self.status = None
        self.deadline = time.monotonic() + 25

    def pump(self):
        if time.monotonic() >= self.deadline:
            raise ProbeFailure("terminal_timeout")
        ready, _, _ = select.select([self.fd], [], [], 0.05)
        if ready:
            try:
                chunk = os.read(self.fd, 65536)
                self.output.extend(chunk)
            except OSError as error:
                if error.errno not in (errno.EIO, errno.EAGAIN):
                    raise
        if self.status is None:
            reaped, status = os.waitpid(self.pid, os.WNOHANG)
            if reaped:
                self.status = status

    def wait_for(self, marker):
        while marker not in self.output:
            self.pump()
            if self.status is not None:
                raise ProbeFailure("exit_before_prompt")

    def write(self, value):
        os.write(self.fd, value)

    def finish(self):
        while self.status is None:
            self.pump()
        # Drain output remaining after waitpid reports the process exit.
        for _ in range(4):
            self.pump()
        return os.waitstatus_to_exitcode(self.status), bytes(self.output)

    def close(self):
        if self.status is None:
            try:
                os.kill(self.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            os.waitpid(self.pid, 0)
        os.close(self.fd)


def check(condition, tag="acceptance_condition"):
    if not condition:
        raise ProbeFailure(tag)


def run_terminal(api_path, scenario):
    email = "terminal-" + uuid.uuid4().hex + "@example.test"
    secret = b"Synthetic-Terminal!2468"
    confirmation = b"Different-Terminal!2468"
    arguments = ["dotnet", api_path, "owner", "create", "--email", email]

    if scenario == "stdin-cancel":
        process = subprocess.Popen(arguments + ["--password-stdin"], stdin=subprocess.PIPE,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                   start_new_session=True, bufsize=0)
        try:
            # A consumed byte proves execution reached stdin after the database
            # checks. Keep the pipe open with no newline so the next read blocks.
            process.stdin.write(b"S")
            unread = array.array("i", [1])
            deadline = time.monotonic() + 20
            while True:
                check(process.poll() is None, "exit_before_stdin_read")
                fcntl.ioctl(process.stdin.fileno(), termios.FIONREAD, unread, True)
                if unread[0] == 0:
                    break
                check(time.monotonic() < deadline, "stdin_read_timeout")
                time.sleep(0.02)
            process.send_signal(signal.SIGINT)
            code = process.wait(timeout=10)
            output = process.stdout.read()
            DETAILS.update(exit_code=code, password_echo=secret in output,
                           confirmation_echo=confirmation in output,
                           created_marker=b"Owner account created." in output)
            check(code == 130, "expected_cancel_exit")
            check(b"cancelled" in output.lower(), "missing_cancel_marker")
            check(secret not in output, "password_echo")
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=5)
            process.stdin.close()
            process.stdout.close()
        return

    command = PtyCommand(arguments)
    try:
        command.wait_for(b"Password: ")
        if scenario == "terminal-cancel":
            os.kill(command.pid, signal.SIGINT)
            code, output = command.finish()
            check(code == 130, "expected_cancel_exit")
            check(b"cancelled" in output.lower(), "missing_cancel_marker")
        else:
            command.write(secret + b"\r")
            command.wait_for(b"Confirm password: ")
            command.write((secret if scenario == "terminal-create" else confirmation) + b"\r")
            code, output = command.finish()
            DETAILS.update(exit_code=code, password_echo=secret in output,
                           confirmation_echo=confirmation in output,
                           created_marker=b"Owner account created." in output)
            check(code == (0 if scenario == "terminal-create" else 2), "expected_exit_code")
            check((b"Owner account created." in output) == (scenario == "terminal-create"), "created_marker")
            check(secret not in output, "password_echo")
            check(confirmation not in output, "confirmation_echo")
    finally:
        DETAILS.update(exit_code=(os.waitstatus_to_exitcode(command.status)
                                  if command.status is not None else "running"),
                       password_echo=secret in command.output,
                       confirmation_echo=confirmation in command.output,
                       created_marker=b"Owner account created." in command.output)
        command.close()


if __name__ == "__main__":
    allowed_scenarios = ("terminal-create", "terminal-mismatch", "terminal-cancel", "stdin-cancel")
    scenario = (sys.argv[2] if len(sys.argv) == 3 and sys.argv[2] in allowed_scenarios else "invalid")
    try:
        check(scenario in allowed_scenarios, "invalid_scenario")
        run_terminal(sys.argv[1], scenario)
        print("Terminal acceptance passed: " + scenario)
    except BaseException as error:
        # Captured terminal data may contain credentials if the no-echo condition fails.
        tag = error.tag if isinstance(error, ProbeFailure) else "probe_error"
        print("Terminal acceptance failed: " + scenario + "; condition=" + tag)
        for key, value in DETAILS.items():
            print(key + "=" + str(value))
        sys.exit(1)
