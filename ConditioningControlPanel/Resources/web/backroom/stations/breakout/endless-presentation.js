// Bounded, analytic decoration behind the playfield. No particles accumulate.
export function drawEndlessField(g, s, reduced = false) {
  const board = s.endlessBoard;
  if (!s.endless || !board || s.state !== 'colour') return;
  const accent = board.accent || '#b7eada', time = reduced ? 0 : s.time;
  g.save();g.strokeStyle=accent;g.fillStyle=accent;
  // A little structure at the edge keeps the open centre readable.
  g.globalAlpha=.10+s.sat*.06;g.lineWidth=1;
  for(const sign of [-1,1]) {
    const x=s.w/2+sign*(s.w*.46);
    g.beginPath();g.moveTo(x,s.h*.12);g.lineTo(x,s.h*.64);g.stroke();
    for(let j=0;j<7;j++) {
      const y=s.h*(.15+j*.07), pulse=reduced?1:.65+.35*Math.sin(time*.8+j);
      g.globalAlpha=(.14+.12*pulse)*s.sat;
      g.fillRect(x-2,y-2,4,4);
    }
  }
  if(s.dome && s.well) {
    const well=s.well, hasOrbit=s.pendulums?.some(p=>p.mode==='orbit');
    g.globalAlpha=hasOrbit?.34:.12;g.lineWidth=hasOrbit?2:1;
    const r=well.pull+13;
    for(let j=0;j<4;j++) {
      const a=j*Math.PI/2-time*.16;
      g.beginPath();g.arc(well.x,well.y,r,a,a+.34);g.stroke();
    }
    if(!reduced) for(let j=0;j<18;j++) {
      const a=j*2.39996-time*(hasOrbit?.65:.18), f=(j/18+time*.09)%1;
      const radius=r+45*(1-f);
      g.globalAlpha=Math.sin(f*Math.PI)*(hasOrbit?.45:.18);
      g.fillRect(well.x+Math.cos(a)*radius,well.y+Math.sin(a)*radius,2,2);
    }
  }
  g.restore();
}
