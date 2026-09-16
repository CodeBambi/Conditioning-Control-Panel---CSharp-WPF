export const catalogueStyle=`
body:has(.br-custom-panel:not([hidden])) :is(.br-hint,.br-crosshair,.br-sp,.br-bell){visibility:hidden}
body:has(.br-custom-panel:not([hidden])) .br-nav{right:calc(50vw + 12px)}
.br-custom-panel{position:fixed;right:0;top:0;bottom:0;width:50vw;z-index:30;display:flex;flex-direction:column;box-sizing:border-box;color:#f7e5ef;font:13px/1.35 "Segoe UI",system-ui,sans-serif;pointer-events:auto}
.br-custom-panel:before{content:"";position:absolute;left:-5px;top:0;bottom:0;width:7px;z-index:5;background:#f3d5a0;border:2px solid #160b21;transform:rotate(1deg);pointer-events:none}
.br-custom-panel[hidden],.br-custom-panel [hidden]{display:none!important}
.br-custom-panel button,.br-custom-panel summary{font:inherit;color:inherit;cursor:pointer}
.br-custom-panel button{background:#30203c;border:1px solid #75536d;border-radius:8px;padding:8px}
.br-custom-panel button:hover{background:#523257}.br-custom-panel :is(button,summary):focus-visible{outline:2px solid #78f4dc;outline-offset:2px}
.br-custom-panel button[aria-pressed=true]{background:#713a60;border-color:#edc077;color:#fff0cf}
.br-custom-header{display:flex;align-items:center;justify-content:space-between;gap:8px;padding:8px 12px;background:#24122f}
.br-custom-header h2{font:600 20px/1.2 "Segoe UI",system-ui,sans-serif;color:#f3d5a0;margin:0}
.br-custom-stage{position:relative;flex:1;min-height:100px;overflow:hidden;touch-action:none}
.br-custom-overview{position:absolute;right:8px;top:8px;z-index:3;background:#21142ed9!important;font-size:11px!important}
.br-custom-items{order:0;display:flex;gap:5px;padding:5px 12px;background:#160d24}.br-custom-items button{flex:1;padding:6px}
.br-custom-stage{order:1}.br-custom-hud{order:2;padding:8px 12px;background:linear-gradient(#291735,#160d24);position:relative}
.br-custom-hud h3{font:600 17px/1.25 "Segoe UI",system-ui,sans-serif;color:#f2d19c;margin:0 0 5px}
.br-custom-actions{display:flex;gap:5px;margin-top:6px}.br-custom-actions button{flex:1;min-width:0;font-size:12px}
.br-custom-more{font-size:12px}.br-custom-more summary{padding:5px 0;color:#d6c0df}
.br-custom-more[open]{position:absolute;bottom:100%;left:10px;right:10px;z-index:6;padding:10px;background:#24122ff5;border:1px solid #bd936c;border-radius:12px;box-shadow:0 -8px 25px #0008}
.br-custom-footer{order:3;padding:4px 12px;background:#160d24;text-align:right}.br-custom-footer button{padding:3px;border:0;background:transparent;font-size:11px;color:#b8a4bf}
.br-custom-remove{display:block;width:100%;margin-top:7px;font-size:13px}
.br-custom-note{font-size:12px;color:#e1c8dc;margin:4px 0}.br-custom-prompt{margin:0;color:#d8c3e1}
.br-custom-palettes button:nth-child(1){border-bottom:3px solid #ef26aa}.br-custom-palettes button:nth-child(2){border-bottom:3px solid #13bea5}.br-custom-palettes button:nth-child(3){border-bottom:3px solid #f47939}
/* The lever arrows sit on the room pane, the part of the screen the sheet leaves free. */
.br-custom-arrows{position:fixed;left:0;top:0;right:50vw;bottom:0;z-index:29;pointer-events:none;font:13px/1.35 "Segoe UI",system-ui,sans-serif;color:#f7e5ef}
.br-custom-arrows[hidden]{display:none!important}
.br-custom-arrows:focus-visible{outline:2px solid #78f4dc;outline-offset:-4px}
.br-custom-arrow{position:absolute;top:50%;transform:translateY(-50%);width:48px;height:48px;pointer-events:auto;cursor:pointer;border-radius:50%;border:2px solid #edc077;background:#21142ed9;color:#f3d5a0;font:28px/1 "Segoe UI",system-ui,sans-serif;padding:0 0 4px;box-shadow:0 2px 12px #0009;touch-action:manipulation}
.br-custom-arrow:hover{background:#523257}.br-custom-arrow:active{background:#713a60}.br-custom-arrow:focus-visible{outline:2px solid #78f4dc;outline-offset:2px}
.br-custom-arrow.is-prev{left:10px}.br-custom-arrow.is-next{right:10px}
.br-custom-centred{position:absolute;left:50%;bottom:10px;transform:translateX(-50%);padding:4px 12px;border-radius:999px;background:#21142ed9;border:1px solid #75536d;color:#f2d19c;font:600 13px/1.35 "Segoe UI",system-ui,sans-serif;white-space:nowrap}
.br-custom-panel .br-vending-pick{position:absolute;z-index:2;padding:0;border:2px solid transparent;background:transparent;border-radius:6px}
.br-custom-panel .br-vending-pick:hover{background:transparent;border-color:transparent}
.br-custom-panel .br-vending-pick[aria-pressed=true]{background:#78f4dc12;border-color:#78f4dc;box-shadow:0 0 9px #78f4dc88}
@media(max-width:700px) and (orientation:portrait){
 body:has(.br-custom-panel:not([hidden])) .br-nav{right:12px}
 .br-custom-panel{left:0;width:auto;top:auto;height:56vh}
 .br-custom-arrows{right:0;bottom:56vh}
 .br-custom-panel:before{left:-2%;right:-2%;top:-5px;bottom:auto;width:auto;height:7px;transform:rotate(-1.5deg)}
 .br-custom-header{padding:7px 12px}.br-custom-header h2{font-size:18px}.br-custom-header button{padding:5px 9px}
 .br-custom-hud{padding:6px 12px}.br-custom-hud h3{font-size:17px}
}
@media(max-height:500px){.br-custom-header{padding:4px 8px}.br-custom-header h2{font-size:17px}.br-custom-header button{padding:4px 8px}.br-custom-items{padding:3px 8px}.br-custom-items button{padding:4px}.br-custom-hud{padding:5px 8px}.br-custom-hud h3{font-size:16px}.br-custom-actions{margin-top:4px}.br-custom-actions button{padding:5px}.br-custom-footer{padding:2px 8px}}
`;
