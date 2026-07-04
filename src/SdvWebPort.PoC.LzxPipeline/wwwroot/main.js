import { dotnet } from './_framework/dotnet.js'
const api = await dotnet.create();
if (typeof globalThis.Module === 'undefined' && api.Module) globalThis.Module = api.Module;
if (typeof globalThis.Blazor === 'undefined') globalThis.Blazor = { runtime: { Module: globalThis.Module } };
await api.runMain();
