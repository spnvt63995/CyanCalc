# Public regression checks

These checks use generated workbooks and the synthetic fixtures included in this repository.

| Location | Purpose |
| --- | --- |
| `CyanCalc.CoreChecks` | .NET/native API, scalar readers, reference utilities, hyperlinks, workbook storage, and keyboard policy |
| `../bindings/dotnet/native/src/protocol.rs` | Rust transport and file compatibility regressions |
| `fixtures` | Synthetic files from a known older engine, used to verify the file-format boundary |

From the repository root:

```sh
dotnet build CyanCalc.slnx -p:RestoreLockedMode=true
dotnet run --project tests/CyanCalc.CoreChecks --no-build
cargo test --locked --manifest-path bindings/dotnet/native/Cargo.toml --lib
```

CoreChecks is a console runner: a failing assertion exits with a nonzero status.
Running it without options executes every public check group. Storage checks
create isolated temporary directories; they do not use the desktop user's library.

The IC/XLSX fixtures exercise compatibility with a specific engine schema.
Their engine identifier and hashes identify the serialized inputs.
See [fixture provenance](fixtures/README.md).

These checks do not exercise native GUI focus, IME, clipboard, or platform dialogs.
