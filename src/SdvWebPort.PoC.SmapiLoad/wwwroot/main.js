// SdvWebPort.PoC.SmapiLoad main.js — Blazor WebAssembly bootstrap
import { dotnet } from './_framework/dotnet.js'

const { runMain } = await dotnet.create();
await runMain();
