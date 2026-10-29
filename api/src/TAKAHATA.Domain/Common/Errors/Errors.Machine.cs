using ErrorOr;

namespace TAKAHATA.Domain.Common.Errors
{
    public partial class Errors
    {
        public static class Machine
        {
            public static Error DuplicateMachine =>
                Error.Validation(code: "Machine.DuplicateMachine", description: "Tên hoặc mã máy đã tồn tại.");

            public static Error CreateFalse =>
                Error.Failure(code: "Machine.CreateFalse", description: "Tạo mới máy không thành công.");

            public static Error UpdateFalse =>
                Error.Failure(code: "Machine.UpdateFalse", description: "Cập nhật máy không thành công.");

            public static Error DeleteFalse =>
                Error.Failure(code: "Machine.DeleteFalse", description: "Xóa máy không thành công.");

            public static Error NotFound =>
                Error.NotFound(code: "Machine.NotFound", description: "Không tìm thấy máy.");

            public static Error IsProcessing =>
                Error.Conflict(code: "Machine.IsProcessing", description: "Máy đang gia công chi tiết khác vui lòng kiểm tra lại.");

            public static Error IsProcessingPLC =>
                Error.Conflict(code: "Machine.IsProcessingPLC", description: "Máy đang gia công vui lòng dừng máy trước khi thao tác.");

            public static Error TimeOut =>
                Error.NotFound(code: "Machine.TimeOut", description: "Không thể kết nối với PLC/Máy.");
        }
    }

}
