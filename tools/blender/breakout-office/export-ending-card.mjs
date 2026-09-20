import {createRequire} from 'node:module';
import {writeFileSync} from 'node:fs';
import {paintEndingCard} from '../../../ConditioningControlPanel/Resources/web/backroom/stations/breakout/ending-card.js';
const require=createRequire(import.meta.url);
const {createCanvas}=require(process.env.BREAKOUT_CANVAS_MODULE||'@napi-rs/canvas');
const canvas=createCanvas(1280,720);paintEndingCard(canvas.getContext('2d'));
writeFileSync(new URL('./assets/ending-screen.png',import.meta.url),canvas.toBuffer('image/png'));
