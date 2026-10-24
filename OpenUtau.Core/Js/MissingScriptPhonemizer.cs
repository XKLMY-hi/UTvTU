using System;
using OpenUtau.Api;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Js {
    // Retain the saved identity when registration is pending or a script is unavailable.
    internal sealed class MissingScriptPhonemizer : Phonemizer {
        readonly string id;
        public MissingScriptPhonemizer(string id) { this.id = id; Name = id; Tag = "MISSING"; }
        public override string Id => id;
        public override void SetSinger(OpenUtau.Core.Ustx.USinger singer) { }
        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            string package = id.Substring(3).Split('/')[0];
            throw new MissingPackageException(package, new InvalidOperationException($"JavaScript phonemizer {id} is unavailable. Check the developer log or reinstall its package."));
        }
    }
}
