// SP-115 mutation sweep. Each mutation is applied to the committed tree, the closed suite is run,
// and the file is restored with `git checkout --` so the restore is byte-identical. Nothing is
// committed. Raw output goes to sweep-roundN.log beside this file.
import { execFileSync, spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync, appendFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '..', '..');
const log = resolve(here, process.argv[3] ?? 'sweep-round1.log');

const SRC = 'client/src/CcpClient.Desktop/';
const M = [
  // ---- the frame type's invariants ----
  ['M-a', SRC + 'Glyph/GlyphFrame.cs',
    'if (blue > alpha || green > alpha || red > alpha)', 'if (false)'],
  ['M-b', SRC + 'Glyph/GlyphFrame.cs',
    'if (alpha == 255 && (blue | green | red) != 0', 'if (alpha >= 0 && true'],
  ['M-c', SRC + 'Glyph/GlyphFrame.cs',
    'var alpha = Scale(_pixels[offset + 3], constantAlpha);',
    'var alpha = _pixels[offset + 3] * constantAlpha / 255;'],
  ['M-d', SRC + 'Glyph/GlyphFrame.cs',
    'private static int Scale(int value, int factor) => ((value * factor) + 127) / 255;',
    'private static int Scale(int value, int factor) => value * factor / 255;'],
  ['M-e', SRC + 'Glyph/GlyphFrame.cs',
    'premultiplied[i] = (byte)(straight[i] * alpha / 255);', 'premultiplied[i] = 0;'],

  // ---- the request type ----
  ['M-f', SRC + 'Glyph/GlyphSurfaceRequest.cs',
    'ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(opacity, 0.0, nameof(opacity));', ''],
  ['M-g', SRC + 'Glyph/GlyphSurfaceRequest.cs',
    'return (byte)Math.Clamp(scaled, MinimumConstantAlpha, byte.MaxValue);',
    'return (byte)Math.Clamp(scaled, 0, byte.MaxValue);'],

  // ---- the surface's gates ----
  ['M-h', SRC + 'Glyph/Win32GlyphSurface.cs',
    'if (!frame.HasProvableInk)\n        {\n            return NoProvableInk(frame);\n        }\n\n        var window = EnsureWindow',
    'if (false)\n        {\n            return NoProvableInk(frame);\n        }\n\n        var window = EnsureWindow'],
  ['M-i', SRC + 'Glyph/Win32GlyphSurface.cs',
    'if (Win32GlyphInterop.GetLayeredWindowAttributes(window, out _, out var uniformAlpha, out var uniformFlags))',
    'if (false && Win32GlyphInterop.GetLayeredWindowAttributes(window, out _, out var uniformAlpha, out var uniformFlags))'],
  ['M-j', SRC + 'Glyph/Win32GlyphSurface.cs',
    '        var content = ConfirmContent(frame);\n        if (content is not null)\n        {\n            return content;\n        }\n',
    '        var content = ConfirmContent(frame);\n        if (false)\n        {\n            return content;\n        }\n'],
  ['M-k', SRC + 'Glyph/Win32GlyphSurface.cs',
    '        var zOrder = ReadZOrder(window);\n        if (!zOrder.AboveEveryOrdinaryWindow)',
    '        var zOrder = ReadZOrder(window);\n        if (false)'],
  ['M-l', SRC + 'Glyph/Win32GlyphSurface.cs',
    '        var routing = ConfirmInputRouting(request);\n        if (routing is not null)',
    '        var routing = ConfirmInputRouting(request);\n        if (false)'],
  ['M-m', SRC + 'Glyph/Win32GlyphSurface.cs',
    'if (foreground == window)', 'if (false)'],
  ['M-n', SRC + 'Glyph/Win32GlyphSurface.cs',
    'if (held != request.Bounds)', 'if (false)'],
  ['M-o', SRC + 'Glyph/Win32GlyphSurface.cs',
    'if ((exStyle & required) != required)', 'if (false)'],
  ['M-p', SRC + 'Glyph/Win32GlyphSurface.cs',
    'if (!Win32GlyphInterop.IsWindowVisible(window))', 'if (false)'],
  ['M-q', SRC + 'Glyph/Win32GlyphSurface.cs',
    'foreach (var (x, y) in frame.ProvableInk)\n        {\n            var held = ReadBack(x, y);',
    'foreach (var (x, y) in Array.Empty<(int, int)>())\n        {\n            var held = ReadBack(x, y);'],
  ['M-r', SRC + 'Glyph/Win32GlyphSurface.cs',
    '                if (frame.AlphaAt(x, y) != 0)\n                {\n                    continue;\n                }',
    '                if (true)\n                {\n                    continue;\n                }'],
  ['M-s', SRC + 'Glyph/Win32GlyphSurface.cs',
    'if (!Win32GlyphInterop.PrintWindow(_window, _readbackDc, Win32GlyphInterop.PwRenderfullcontent))',
    'if (false)'],
  ['M-t', SRC + 'Glyph/Win32GlyphSurface.cs',
    'if (held != bounds)\n        {\n            return Unavailable(GlyphReasonCodes.GlyphGeometryRefused,\n                $"the OS holds {held} for window 0x{_window:X} after a move',
    'if (false)\n        {\n            return Unavailable(GlyphReasonCodes.GlyphGeometryRefused,\n                $"the OS holds {held} for window 0x{_window:X} after a move'],
  ['M-u', SRC + 'Glyph/Win32GlyphSurface.cs',
    'if (bounds.Width != _current.Bounds.Width || bounds.Height != _current.Bounds.Height)', 'if (false)'],
  ['M-v', SRC + 'Glyph/Win32GlyphSurface.cs',
    'if (frame.Width != bounds.Width || frame.Height != bounds.Height)', 'if (false)'],
  ['M-w', SRC + 'Glyph/Win32GlyphSurface.cs',
    'if (Win32GlyphInterop.IsWindowVisible(_window))\n        {\n            return Unavailable(GlyphReasonCodes.GlyphWithdrawRefused,',
    'if (false)\n        {\n            return Unavailable(GlyphReasonCodes.GlyphWithdrawRefused,'],
  ['M-x', SRC + 'Glyph/Win32GlyphSurface.cs',
    'ApplyClickThroughStyle(window, clickThrough: false);\n        var opaqueWinner = HitTest(point, expectSurface: true, out var opaqueAttempts);',
    'var opaqueWinner = window;\n        var opaqueAttempts = 1;'],
  ['M-y', SRC + 'Glyph/Win32GlyphSurface.cs',
    'if (request.ClickThrough && finalWinner == window)', 'if (false)'],
  ['M-z', SRC + 'Glyph/Win32GlyphSurface.cs',
    '            | Win32GlyphInterop.WsExLayered\n            | Win32GlyphInterop.WsExToolwindow',
    '            | Win32GlyphInterop.WsExToolwindow'],
  ['M-aa', SRC + 'Glyph/Win32GlyphSurface.cs',
    'biHeight = -height,', 'biHeight = height,'],
  ['M-ab', SRC + 'Glyph/Win32GlyphSurface.cs',
    'AlphaFormat = Win32GlyphInterop.AcSrcAlpha,', 'AlphaFormat = 0,'],
  ['M-ac', SRC + 'Glyph/Win32GlyphSurface.cs',
    'SourceConstantAlpha = constantAlpha,', 'SourceConstantAlpha = 255,'],
  ['M-ad', SRC + 'Glyph/Win32GlyphSurface.cs',
    'Win32GlyphInterop.WsExLayered | Win32GlyphInterop.WsExTransparent\n                | Win32GlyphInterop.WsExToolwindow | Win32GlyphInterop.WsExNoactivate,',
    'Win32GlyphInterop.WsExTransparent\n                | Win32GlyphInterop.WsExToolwindow | Win32GlyphInterop.WsExNoactivate,'],
  ['M-ae', SRC + 'Glyph/Win32GlyphSurface.cs',
    'Index >= 0 && (FirstOrdinaryIndex < 0 || Index < FirstOrdinaryIndex);', 'Index >= 0;'],

  // ---- the factory ----
  ['M-af', SRC + 'Glyph/GlyphSurfaceFactory.cs',
    'never composite into a surface nobody can confirm. " + LinuxManualGate),',
    'never composite into a surface nobody can confirm."),'],
  ['M-af2', SRC + 'Glyph/GlyphSurfaceFactory.cs',
    '+ "(2) a compositing manager is RUNNING (the _NET_WM_CM_S0 selection has an owner), because without one "',
    '+ "(2) "'],
  ['M-ag', SRC + 'Glyph/UnsupportedGlyphSurface.cs',
    'public CapabilityState MoveTo(GlyphBounds bounds) => new CapabilityState.Unavailable(_reason);',
    'public CapabilityState MoveTo(GlyphBounds bounds) => new CapabilityState.Available("moved");'],
  ['M-ah', SRC + 'Glyph/UnsupportedGlyphSurface.cs',
    'public CapabilityState Withdraw() => new CapabilityState.Unavailable(_reason);',
    'public CapabilityState Withdraw() => new CapabilityState.Available("withdrawn");'],

  // ---- the motion ----
  ['M-ai', SRC + 'Effects/BouncingTextField.cs',
    'if (!_started)\n        {\n            _started = true;\n            return false;\n        }', 'if (false)\n        {\n            _started = true;\n            return false;\n        }'],
  ['M-aj', SRC + 'Effects/BouncingTextField.cs',
    'var step = Math.Min(elapsedSeconds, MaxStepSeconds);', 'var step = elapsedSeconds;'],
  ['M-ak', SRC + 'Effects/BouncingTextField.cs',
    'if (double.IsNaN(elapsedSeconds) || elapsedSeconds <= 0)', 'if (false)'],
  ['M-al', SRC + 'Effects/BouncingTextField.cs',
    '            _x = minX;\n            _velocityX = Math.Abs(_velocityX);', '            _velocityX = Math.Abs(_velocityX);'],
  ['M-am', SRC + 'Effects/BouncingTextField.cs',
    '            _x = maxX - _width;\n            _velocityX = -Math.Abs(_velocityX);', '            _velocityX = -Math.Abs(_velocityX);'],
  ['M-an', SRC + 'Effects/BouncingTextField.cs',
    '            _y = maxY - _height;\n            _velocityY = -Math.Abs(_velocityY);', '            _velocityY = -Math.Abs(_velocityY);'],
  ['M-ao', SRC + 'Effects/BouncingTextField.cs',
    'if ((bouncedX && bouncedY) || IsNearCorner(minX, minY, maxX, maxY))', 'if (false)'],
  ['M-ap', SRC + 'Effects/BouncingTextField.cs',
    '        _colour = NextColour(rerolling: true);\n        NeedsRaster = true;', '        _colour = NextColour(rerolling: true);'],
  ['M-aq', SRC + 'Effects/BouncingTextField.cs',
    'if (_random.NextDouble() < TextChangeChanceOnBounce)', 'if (false)'],
  ['M-ar', SRC + 'Effects/BouncingTextField.cs',
    'var speed = _presentation.SpeedSetting / 10.0;', 'var speed = 1.0;'],
  ['M-as', SRC + 'Effects/BouncingTextField.cs',
    'return enabled.Count == 0 ? FallbackText : enabled[_random.Next(enabled.Count)];',
    'return enabled.Count == 0 ? "" : enabled[_random.Next(enabled.Count)];'],
  ['M-at', SRC + 'Effects/BouncingTextField.cs',
    '                if (rerolling)\n                {\n                    _hueIndex = (_hueIndex + 1) % RainbowWheel.Length;\n                }',
    '                if (rerolling)\n                {\n                    _hueIndex = _random.Next(RainbowWheel.Length);\n                }'],
  ['M-au', SRC + 'Effects/BouncingTextField.cs',
    'return _presentation.FixedColour ?? HotPink;', 'return _presentation.FixedColour ?? 0x00000000;'],

  // ---- the presentation ----
  ['M-av', SRC + 'Effects/BouncingTextPresentation.cs',
    'public int FontSize => Math.Max(1, (int)(BaseFontSize * SizePercent / 100.0));',
    'public int FontSize => BaseFontSize;'],
  ['M-aw', SRC + 'Effects/BouncingTextPresentation.cs',
    'public double? SurfaceOpacity => OpacityPercent <= 0 ? null : Math.Clamp(OpacityPercent / 100.0, 0.0, 1.0);',
    'public double? SurfaceOpacity => Math.Clamp(Math.Max(0.01, OpacityPercent / 100.0), 0.0, 1.0);'],

  // ---- the presenter ----
  ['M-ax', SRC + 'Effects/BouncingTextSurfacePresenter.cs',
    'var state = field.NeedsRaster ? PlaceCurrent(firstPlacement: false) : MoveCurrent();',
    'var state = PlaceCurrent(firstPlacement: false);'],
  ['M-ay', SRC + 'Effects/BouncingTextSurfacePresenter.cs',
    '        if (!held)\n        {', '        if (false)\n        {'],
  ['M-az', SRC + 'Effects/BouncingTextSurfacePresenter.cs',
    'return _showing && _lastFrameHeld && _frameTimer is not null;', 'return _showing;'],
  ['M-ba', SRC + 'Effects/BouncingTextSurfacePresenter.cs',
    '        timer?.Dispose();\n        if (surface is not null)', '        if (surface is not null)'],
  ['M-bb', SRC + 'Effects/BouncingTextSurfacePresenter.cs',
    '        if (opacity is null)\n        {\n            Withdraw();', '        if (false)\n        {\n            Withdraw();'],
  ['M-bc', SRC + 'Effects/BouncingTextSurfacePresenter.cs',
    '        if (display is null)\n        {\n            Withdraw();', '        if (false)\n        {\n            Withdraw();'],
  ['M-bd', SRC + 'Effects/BouncingTextSurfacePresenter.cs',
    '        if (frame is null)\n        {\n            return new CapabilityState.Degraded(', '        if (false)\n        {\n            return new CapabilityState.Degraded('],
  ['M-be', SRC + 'Effects/BouncingTextSurfacePresenter.cs',
    'presentation.SurfaceOpacity ?? 1.0,', '1.0,'],
  ['M-bf', SRC + 'Effects/BouncingTextSurfacePresenter.cs',
    '            _lastFrameHeld = true;', '            _lastFrameHeld = false;'],

  // ---- the module ----
  ['M-bg', SRC + 'Effects/BouncingTextEffect.cs',
    'protected override bool WorkIsRunning => _surface is { Running: true };',
    'protected override bool WorkIsRunning => _surface is { Showing: true };'],
  ['M-bh', SRC + 'Effects/BouncingTextEffect.cs',
    '        if (presentation.IsInvisible)\n        {\n            _surface?.Withdraw();', '        if (false)\n        {\n            _surface?.Withdraw();'],
  ['M-bi', SRC + 'Effects/BouncingTextEffect.cs',
    '        if (engaged is CapabilityState.Available healthy)\n        {\n            return new CapabilityState.Degraded(',
    '        if (false)\n        {\n            return new CapabilityState.Degraded('],
  ['M-bj', SRC + 'Effects/BouncingTextEffect.cs',
    '                    degraded.Reason.Detail + " " + TransformsAbsentNotice));', '                    degraded.Reason.Detail));'],
  ['M-bk', SRC + 'Effects/BouncingTextEffect.cs',
    'if (_surface is not { Engaged: true })', 'if (_surface is not { Showing: true })'],
  ['M-bl', SRC + 'Effects/BouncingTextEffect.cs',
    'if (!Signal.IsBound)', 'if (false)'],
  ['M-bm', SRC + 'Effects/BouncingTextEffect.cs',
    'public const string DisplayTitle = "Bouncing Text (motion half)";',
    'public const string DisplayTitle = "Bouncing Text";'],

  // ---- the rasteriser ----
  ['M-bn', SRC + 'Effects/GlyphTextSource.cs',
    'return frame.HasProvableInk ? frame : null;', 'return frame;'],
  ['M-bo', SRC + 'Effects/GlyphTextSource.cs',
    'if (GdiPlus.GdipGraphicsClear(graphics, 0x00000000) != Ok', 'if (GdiPlus.GdipGraphicsClear(graphics, 0xFF000000) != Ok'],
  ['M-bp', SRC + 'Effects/GlyphTextSource.cs',
    'private const int PixelFormat32bppPargb = 0x000E200B;', 'private const int PixelFormat32bppPargb = 0x0026200A;'],
  ['M-bq', SRC + 'Effects/GlyphTextSource.cs',
    'if (!TryRepairRounding(pixels))\n        {\n            return null;\n        }', 'if (false)\n        {\n            return null;\n        }'],
  ['M-bs', SRC + 'Effects/GlyphTextSource.cs',
    'if (value - alpha > MaxRoundingOverage)', 'if (false)'],
  ['M-br', SRC + 'Effects/GlyphTextSource.cs',
    ': (size.Value.Width + (2 * Margin), size.Value.Height + (2 * Margin));', ': (size.Value.Width, size.Value.Height);'],
];

function run(cmd, args) {
  return spawnSync(cmd, args, { cwd: root, encoding: 'utf8', shell: false, maxBuffer: 64 * 1024 * 1024 });
}

const filter = (process.argv[2] ?? '').split(',').filter(Boolean);
const chosen = filter.length ? M.filter(m => filter.includes(m[0])) : M;

const suites = [
  '--filter',
  'FullyQualifiedName~Glyph|FullyQualifiedName~BouncingText|FullyQualifiedName~ContinuousEffectSpine|FullyQualifiedName~SecondEffectSpine|FullyQualifiedName~AudioModuleSpine|FullyQualifiedName~SessionSpine|FullyQualifiedName~StudioSurfaceNotice|FullyQualifiedName~MovingEffectSpine',
];

writeFileSync(log, `SP-115 sweep, ${chosen.length} mutation(s)\n`);
let caught = 0;
let survived = 0;
let notPatched = 0;

for (const [id, file, needle, replacement] of chosen) {
  const path = resolve(root, file);
  // The working tree is CRLF (git normalises on checkout) while every needle here is written with
  // LF, so round 1 failed to patch 22 of 70 mutations for a reason that had nothing to do with the
  // code under test. Matching happens on an LF-normalised copy and the file is written back as LF;
  // `git checkout --` restores the canonical CRLF afterwards, which is why round 1 also reported
  // "restore drift" on the first mutation of every file. `git status` was clean after both rounds.
  const before = readFileSync(path, 'utf8').replaceAll('\r\n', '\n');
  if (!before.includes(needle)) {
    notPatched++;
    appendFileSync(log, `${id}  NOT-PATCHED (needle absent) ${file}\n`);
    continue;
  }

  writeFileSync(path, before.replace(needle, replacement));
  const build = run('dotnet', ['build', 'client/tests/CcpClient.Tests/CcpClient.Tests.csproj', '-c', 'Debug', '--nologo']);
  let verdict;
  let detail;
  if (build.status !== 0) {
    verdict = 'CAUGHT';
    detail = 'compile error';
  } else {
    const test = run('dotnet', ['test', 'client/tests/CcpClient.Tests/CcpClient.Tests.csproj', '-c', 'Debug', '--nologo', '--no-build', ...suites]);
    const tail = (test.stdout ?? '').split('\n').filter(l => /Failed!|Passed!/.test(l)).join(' ');
    verdict = test.status === 0 ? 'SURVIVED' : 'CAUGHT';
    detail = tail.trim();
  }

  if (verdict === 'CAUGHT') { caught++; } else { survived++; }
  appendFileSync(log, `${id}  ${verdict}  ${file}  ${detail}\n`);
  console.log(`${id} ${verdict} ${detail}`);

  execFileSync('git', ['checkout', '--', file], { cwd: root });
  const after = readFileSync(path, 'utf8').replaceAll('\r\n', '\n');
  if (after !== before) {
    appendFileSync(log, `${id}  !! RESTORE DRIFT !!\n`);
    console.error(`${id} RESTORE DRIFT`);
  }
}

const summary = `\nTOTAL ${chosen.length}: caught ${caught}, survived ${survived}, not-patched ${notPatched}\n`;
appendFileSync(log, summary);
console.log(summary);
