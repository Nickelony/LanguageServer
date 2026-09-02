# Nickelony.IDEKit.Tooling

External-process execution for compiler, formatter, linter, and generator
integration in text editors.

The package launches external processes from immutable request records and
returns immutable outcome records. The runner owns the orchestration that
integrations used to repeat by hand: bounded waits with timeouts, cancellation,
and process-tree termination with a fallback to a single-process kill for
shell-launched batches. It is not a thin mirror of `System.Diagnostics.Process`:
callers describe what they want to run and the runner drives the process to a
terminal outcome.

The package is dependency-free and stays out of the rest of the TextEditor
family, so a host can adopt process execution without coupling its text
primitives to any framework.

- `ProcessRunRequest` — immutable run description: executable, arguments,
  working directory, environment variables, output redirection with encodings,
  shell execution, and an optional timeout.
- `ProcessRunner` / `IProcessRunner` — `Run` drives a request to completion and
  returns a `ProcessRunResult` (started, exit code, captured output, timed-out,
  cancelled); `Start` returns an `IProcessHandle` for callers that drive the
  process lifetime themselves.
- `ProcessRunResult` — immutable outcome with explicit started / timed-out /
  cancelled flags.
- `IProcessHandle` — waiting, killing, and whole-tree termination for
  caller-driven process lifetime.

Hosts keep their own workflows (compiler batch generation, staging
directories, output copying, and log-file interpretation) and type them against
`IProcessRunner`, which makes the process orchestration testable without
launching real processes.
