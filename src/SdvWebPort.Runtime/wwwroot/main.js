// SdvWebPort.Runtime main.js — Blazor WebAssembly bootstrap
import { dotnet } from './_framework/dotnet.js'

const { runMain } = await dotnet.create();

// runMain executes the C# Main() method
await runMain();
