export const STYLE = `
.ef{--ink:#f7edf8;--muted:#c4b4cc;color:var(--ink);font:16px/1.4 system-ui,sans-serif;box-sizing:border-box;
position:relative;isolation:isolate;width:100%;max-width:620px;margin:auto;min-height:480px;padding:20px;
border-radius:24px;overflow:hidden;background:radial-gradient(ellipse at 50% 28%,#413453 0,#211c32 60%,#151422)}
.ef *{box-sizing:border-box}.ef button{font:inherit;color:inherit;cursor:pointer;touch-action:manipulation}
.ef-head{display:flex;justify-content:space-between;gap:12px;align-items:center;font-size:13px;color:var(--muted)}
.ef h2{font-size:clamp(24px,6vw,34px);margin:18px 0 4px;text-align:center;font-weight:650;letter-spacing:-.04em}
.ef-copy{text-align:center;margin:0 auto 18px;color:var(--muted);min-height:44px;max-width:360px}
.ef-layers{display:flex;justify-content:center;gap:8px;height:26px;align-items:center}
.ef-layer{width:28px;height:6px;border-radius:8px;background:#78638644;transition:background .2s,box-shadow .2s}
.ef-layer[data-on="true"]{background:#eebcde;box-shadow:0 0 15px #eebcde55}
.ef-beats{display:flex;justify-content:center;gap:12px;height:36px;align-items:center;margin-bottom:14px}
.ef-beat{width:8px;height:8px;border:1px solid #b7a4c5;border-radius:50%}.ef-beat[data-on="true"]{background:#fff1dd;box-shadow:0 0 12px #f5cfa7}
.ef-pads{display:grid;grid-template-columns:1fr 1fr;gap:20px;max-width:400px;margin:auto}
.ef-pad{position:relative;padding:0;background:none;border:0;min-height:118px;aspect-ratio:1.2;outline-offset:6px;border-radius:28px}
.ef-pad:focus-visible{outline:3px solid white}.ef-face{display:flex;align-items:center;justify-content:center;flex-direction:column;
height:100%;border-radius:28px;background:linear-gradient(145deg,color-mix(in srgb,var(--pad) 40%,#393044),#292334);
border:1px solid color-mix(in srgb,var(--pad) 65%,transparent);box-shadow:0 7px 0 #10101b,0 12px 28px #0a07183b;
transition:transform 70ms,box-shadow 160ms;color:var(--pad)}
.ef-symbol{font-size:40px;line-height:1}.ef-pad small{font-size:12px;margin-top:10px;color:#f6edf8bd}
.ef-pad[data-pressed="true"] .ef-face{transform:translateY(5px) scale(.97);box-shadow:0 2px 0 #10101b,0 0 30px color-mix(in srgb,var(--pad) 25%,transparent)}
.ef-pad[data-lit="true"] .ef-face{background:linear-gradient(145deg,color-mix(in srgb,var(--pad) 65%,#393044),#393044);color:#fff}
.ef-ring{pointer-events:none;position:absolute;inset:8px;border:3px solid var(--pad);border-radius:22px;opacity:0;transform:scale(.6)}
.ef-pad[data-cue="true"] .ef-ring{opacity:1;transform:scale(var(--arrival,.6));box-shadow:0 0 14px color-mix(in srgb,var(--pad) 40%,transparent)}
.ef-pad[data-hit="true"]::after{content:'+';position:absolute;inset:0;color:#fff7df;font-size:36px;pointer-events:none;animation:ef-spark .45s ease-out both}
.ef-hint{min-height:30px;text-align:center;font-size:14px;margin:20px 0 0;color:#edd5e9}
.ef-overlay{position:absolute;inset:0;z-index:5;display:flex;align-items:center;justify-content:center;flex-direction:column;text-align:center;
padding:28px;background:#211c32ed;backdrop-filter:blur(8px)}.ef-overlay[hidden]{display:none}
.ef-overlay h2{margin:0 0 8px}.ef-overlay p{color:var(--muted);max-width:320px;margin:0 0 24px}
.ef-action{min-height:52px;min-width:160px;border-radius:16px;border:1px solid #e9bad3;background:#674660;padding:12px 22px}
.ef-choices{display:flex;gap:12px;flex-wrap:wrap;justify-content:center}.ef-action:active{transform:scale(.97)}
.ef[data-finale="true"]{box-shadow:inset 0 0 90px #e9bad344}.ef-progress{font-variant-numeric:tabular-nums}
@keyframes ef-spark{from{opacity:1;transform:translateY(-12px) scale(.5)}to{opacity:0;transform:translateY(-60px) scale(1.2)}}
.ef[data-reduced="true"] *{animation:none!important;transition:none!important}.ef[data-reduced="true"] .ef-face{transform:none!important}
.ef[data-reduced="true"] .ef-ring{transform:none!important}.ef[data-reduced="true"] .ef-pad[data-hit="true"] .ef-face{outline:3px solid #fff7df}
@media(max-width:380px),(max-height:700px){.ef{padding:14px;min-height:440px}.ef h2{margin-top:12px}.ef-copy{margin-bottom:8px}.ef-pads{gap:16px}.ef-pad{min-height:100px}.ef-hint{margin-top:16px}}
`;
