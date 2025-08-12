using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ScriptPortal.Vegas;
using Core.Domain.Clip;

namespace Core
{

    public class TimelineManager
    {
        private static int markerIndex = 0;

        private readonly List<Clip> _clipList = new List<Clip>();

        public TimelineManager()
        {
            
        }

        public void AddClip(string path)
        {
            _clipList.Add(new Clip { FilePath = path });
        }


        public void AddMarker(Vegas vegas, double seconds)
        {
            Marker marker = new Marker(Timecode.FromSeconds(seconds), markerIndex.ToString());
            marker.IsValid();
            vegas.Project.Markers.Add(marker);
            markerIndex++;
        }
    }
}
