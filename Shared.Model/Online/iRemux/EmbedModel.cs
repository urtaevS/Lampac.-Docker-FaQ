﻿namespace Shared.Model.Online.iRemux
{
    public class EmbedModel
    {
        public string? content { get; set; }

        public List<Similar> similars { get; set; } = new List<Similar>();
    }
}
