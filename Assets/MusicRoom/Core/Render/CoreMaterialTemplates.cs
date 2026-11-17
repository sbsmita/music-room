// Core's material templates (MaterialTemplates.CreateAll finds this provider by reflection, so the Editor folder never
// changes): the contact-shadow blobs (E3) and the two new hand looks of 10-upgrade/experience.md E6, whose shaders live in
// the Hands folder (a leaf assembly without Core, hence "templates from Core's provider"). They are written next to the
// foundation templates, Assets/MusicRoom/Resources/MaterialTemplates/<Name>.mat (Tools/unity.sh render-setup).
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    public sealed class CoreMaterialTemplates : IMaterialTemplateProvider
    {
        public const string ContactShadow = ContactShadowPool.Template, HandSkin = "HandSkin", HandPassthrough = "HandPassthrough";
        public const string ContactShadowShader = "MusicRoom/ContactShadow", HandSkinShader = "MusicRoom/HandSkin", HandPassthroughShader = "MusicRoom/HandPassthrough";
        /// <summary>The cut-out draws last (Overlay - 1), after every transparent of the room.</summary>
        public const int HandPassthroughQueue = 3999;
        public const int HandSkinQueue = 3000;

        public string ResourcesFolder => "Assets/MusicRoom";

        public IEnumerable<MaterialTemplateSpec> Templates
        {
            get
            {
                yield return new MaterialTemplateSpec(ContactShadow, ContactShadowShader, m =>
                {
                    m.SetFloat("_MaxDark", ContactShadowPool.MaxDark);
                    m.renderQueue = ContactShadowPool.Queue;
                });
                yield return new MaterialTemplateSpec(HandSkin, HandSkinShader, m =>
                {
                    m.SetColor("_Color", new Color(0.86f, 0.78f, 0.70f, 1f));
                    m.SetColor("_RimColor", new Color(1.0f, 0.94f, 0.86f, 1f));
                    m.SetFloat("_RimStrength", 0.35f);
                    m.SetFloat("_RimPower", 2.5f);
                    m.SetFloat("_Wrap", 0.5f);
                    m.SetFloat("_MaxOpacity", 0.92f);
                    m.SetFloat("_Alpha", 1f);
                    m.renderQueue = HandSkinQueue;
                });
                yield return new MaterialTemplateSpec(HandPassthrough, HandPassthroughShader, m =>
                {
                    m.SetFloat("_Alpha", 1f);
                    m.SetFloat("_Dilate", 0.0015f);
                    m.SetFloat("_EdgeFrom", 0.05f);
                    m.SetFloat("_EdgeTo", 0.35f);
                    m.renderQueue = HandPassthroughQueue;
                });
            }
        }
    }
}
