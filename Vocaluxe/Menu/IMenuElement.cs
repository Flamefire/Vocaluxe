﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.XPath;

using Vocaluxe.Base;
using Vocaluxe.Lib.Draw;

namespace Vocaluxe.Menu
{
    public interface IMenuElement
    {
        bool LoadTheme(string XmlPath, string ElementName, XPathNavigator navigator, int SkinIndex);
        bool SaveTheme(XmlWriter writer);

        void UnloadTextures();
        void LoadTextures();
        void ReloadTextures();
        
        void MoveElement(int stepX, int stepY);
        void ResizeElement(int stepW, int stepH);
    }
}
