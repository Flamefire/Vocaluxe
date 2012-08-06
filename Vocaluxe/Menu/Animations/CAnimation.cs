﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.XPath;

using Vocaluxe.Base;
using Vocaluxe.Lib.Draw;

namespace Vocaluxe.Menu.Animations
{
    public class CAnimation:IAnimation
    {
        private IAnimation _Animation;

        public CAnimation(EAnimationType Type)
        {
            setAnimation(Type);
        }

        public void Init()
        {
            _Animation.Init();
        }

        public bool LoadAnimation(string item, XPathNavigator navigator)
        {
            return _Animation.LoadAnimation(item, navigator);
        }

        public bool SaveAnimation(XmlWriter writer)
        {
            return _Animation.SaveAnimation(writer);
        }

        public void StartAnimation()
        {
            _Animation.StartAnimation();
        }

        public void StopAnimation()
        {
            _Animation.StopAnimation();
        }

        public void ResetAnimation()
        {
            _Animation.ResetAnimation();
        }

        public bool AnimationActive()
        {
            return _Animation.AnimationActive();
        }

        public void Update()
        {
            _Animation.Update();
        }

        public void setRect(SRectF rect)
        {
            _Animation.setRect(rect);
        }

        public SRectF getRect()
        {
            return _Animation.getRect();
        }

        public float[] getRectChanges()
        {
            return _Animation.getRectChanges();
        }

        public void setColor(SColorF color)
        {
            _Animation.setColor(color);
        }

        public SColorF getColor()
        {
            return _Animation.getColor();
        }

        public void setTexture(ref STexture texture)
        {
            _Animation.setTexture(ref texture);
        }

        public STexture getTexture()
        {
            return _Animation.getTexture();
        }

        public bool isDrawn()
        {
            return _Animation.isDrawn();
        }

        public void setAnimationReset(EOffOn reset)
        {
            _Animation.setAnimationReset(reset);
        }

        public EOffOn getAnimationReset()
        {
            return _Animation.getAnimationReset();
        }

        public void setAnimation(EAnimationType Type)
        {
            switch (Type)
            {
                case EAnimationType.Resize:
                    _Animation = new CAnimationResize();
                    break;

                case EAnimationType.MoveLinear:
                    _Animation = new CAnimationMoveLinear();
                    break;

                case EAnimationType.Video:
                    _Animation = new CAnimationVideo();
                    break;
            }
        }
    }
}
