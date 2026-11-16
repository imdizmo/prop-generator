# The old interactive-session script (needs the MatchCam_* cameras in the open .blend); superseded by match_views.py.
import bpy, numpy as np, os
OUT=os.environ.get("CLIFF_MATCH_OUT", os.path.expanduser("~/.cache/prop-generator/cliffs/match"))
os.makedirs(OUT,exist_ok=True)
def render_views(tag, objname, res=400):
    sc=bpy.context.scene
    keep={objname}
    saved={o.name:o.hide_render for o in bpy.data.objects}
    for o in bpy.data.objects: o.hide_render = o.name not in keep
    sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.display.shading.color_type='SINGLE'
    sc.display.shading.single_color=(0.8,0.8,0.8); sc.display.shading.show_cavity=True; sc.display.shading.cavity_type='BOTH'
    sc.render.film_transparent=True; sc.render.resolution_x=res; sc.render.resolution_y=res
    out={}
    for v in ("front","side","top","persp"):
        sc.camera=bpy.data.objects["MatchCam_"+v]
        p=f"{OUT}/{tag}_{v}.png"; sc.render.filepath=p; bpy.ops.render.render(write_still=True)
        im=bpy.data.images.load(p,check_existing=False); a=np.empty(res*res*4,np.float32); im.pixels.foreach_get(a); bpy.data.images.remove(im)
        out[v]=a.reshape(res,res,4)[::-1]
    for o in bpy.data.objects: o.hide_render=saved[o.name]
    return out
def metrics(T,C):
    r={}
    for v in ("front","side","top"):
        ta=T[v][...,3]>0.5; ca=C[v][...,3]>0.5
        # best overlap over small sideways shifts (the two are centred on their own bounding boxes)
        iou=0.0; best=0
        dys=range(-12,13,2) if v=="top" else (0,)
        for dx in range(-12,13,2):
            for dy in dys:
                cs=np.roll(np.roll(ca,dx,axis=1),dy,axis=0); v_=(ta&cs).sum()/max(1,(ta|cs).sum())
                if v_>iou: iou,best=v_,(dx,dy)
        ca=np.roll(np.roll(ca,best[0],axis=1),best[1],axis=0)
        bands=[]
        if v!="top":
            ys=np.where(ta.any(1))[0]; y0,y1=ys.min(),ys.max()
            for i in range(10):
                a=y0+(y1-y0)*i//10; b=y0+(y1-y0)*(i+1)//10
                tw=ta[a:b].sum(1).mean(); cw=ca[a:b].sum(1).mean()
                bands.append(round(float(cw/max(tw,1)),2))
        # shading difference inside the shared silhouette
        both=ta&ca
        sd=float(np.abs(T[v][...,0]-C[v][...,0])[both].mean()) if both.any() else 1
        r[v]={"iou":round(float(iou),3),"shift_px":best,"shade_diff":round(sd,3),"band_ratio_top_to_bottom":bands}
    return r
def sheet(T,C,name):
    rows=[np.concatenate([X[v] for v in ("front","side","top","persp")],1) for X in (T,C)]
    img=np.concatenate(rows,0); bg=np.array([0.23,0.23,0.25,1],np.float32)
    rgb=img[...,:3]*img[...,3:4]+bg[:3]*(1-img[...,3:4]); h,w=rgb.shape[:2]
    out=np.concatenate([rgb,np.ones((h,w,1),np.float32)],2)[::-1].ravel()
    im=bpy.data.images.new("sheet",w,h); im.pixels.foreach_set(out); im.filepath_raw=f"{OUT}/{name}.png"; im.file_format='PNG'; im.save(); bpy.data.images.remove(im)
