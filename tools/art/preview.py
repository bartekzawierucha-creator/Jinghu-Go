import sys
from PIL import Image, ImageDraw
names=sys.argv[2:]; rows=[]
for n in names:
    for p in ('idle','attack','defeat'):
        im=Image.open(f'out/anim_{n}_{p}.png')
        bg=Image.new('RGBA',im.size,(60,70,80,255)); bg.alpha_composite(im)
        d=ImageDraw.Draw(bg)
        for x in range(0,im.size[0],320): d.line([x,0,x,256],fill=(255,0,0)); d.line([x,246,x+320,246],fill=(255,255,0))
        rows.append(bg.convert('RGB'))
W=max(r.size[0] for r in rows); s=0.25
out=Image.new('RGB',(int(W*s),int(64*len(rows))),(0,0,0))
for i,r in enumerate(rows): out.paste(r.resize((int(r.size[0]*s),64)),(0,i*64))
out.save(sys.argv[1])
