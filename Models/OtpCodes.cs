using System;
using System.Collections.Generic;

namespace iam_service.Modals;

public partial class OtpCodes
{
    public MongoDB.Bson.ObjectId Id { get; set; }

    public string Email { get; set; } = null!;

    public string OtpCode { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public int Status { get; set; }
}
