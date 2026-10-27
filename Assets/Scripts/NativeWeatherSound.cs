using System.Collections.Generic;
using UnityEngine;

// weathersound.cpp: one current voice, 70% SFX trim, rain hysteresis and 1500ms release.
public sealed class NativeWeatherSound : MonoBehaviour
{
    sealed class Voice { public AudioSource Source; public double Release; }
    readonly List<Voice> fading = new List<Voice>();
    readonly HashSet<int> failed = new HashSet<int>();
    AudioSource current;
    int playing;
    public void Tick(int wanted, float rain)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        for (int i=fading.Count-1;i>=0;i--)
        {
            var v=fading[i];
            if(now>=v.Release){v.Source.Stop();Destroy(v.Source);fading.RemoveAt(i);}
            else v.Source.volume=.7f*AudioManager.SfxVolume*(float)((v.Release-now)/1.5);
        }
        if(wanted==1 || playing==1)
        {
            float gate=playing==1?.10f:.35f;
            if(playing==1 && rain>=gate)wanted=1;
            else if(wanted==1 && rain<gate)wanted=0;
        }
        if(current!=null)current.volume=.7f*AudioManager.SfxVolume;
        if(wanted==playing)return;
        if(current!=null){fading.Add(new Voice{Source=current,Release=now+1.5});current=null;}
        playing=0;
        if(wanted==0 || failed.Contains(wanted))return;
        string key=wanted==1?"Weather/rain":"Weather/wind";
        if(!AudioManager.Has(key)||AudioManager.EffectFailed(key)){failed.Add(wanted);return;}
        var clip=AudioManager.EffectClip(key);
        if(clip==null)return; // asynchronous load; no silent voice is installed
        current=gameObject.AddComponent<AudioSource>();current.playOnAwake=false;
        current.clip=clip;current.loop=true;current.pitch=1;current.volume=.7f*AudioManager.SfxVolume;
        current.Play();playing=wanted;
    }
    void OnDisable()
    {
        if(current!=null){current.Stop();Destroy(current);current=null;}
        foreach(var voice in fading)if(voice.Source!=null){voice.Source.Stop();Destroy(voice.Source);}
        fading.Clear();playing=0;
    }
}
