import { getPartProcessByMachineAsync } from "@/app/services/machine/MachineService";
import { ManufacturingProcess } from "@/app/types";
import { ProcessStatus } from "@/app/types/common/ProcessStatus";
import { getDisplayProcessEndTime } from "@/app/utilities/ProcessTime";
import { getStatusTag } from "@/app/utilities/UtilityState";
import { Modal, Table } from "antd";
import { ColumnsType } from "antd/es/table";
import dayjs from "dayjs";
import { useCallback, useEffect, useMemo, useState } from "react";
import { toast } from "react-toastify";

interface DetailPlantProps {
  open: boolean;
  machineId: number | null;
  onClose?: () => void;
}

export default function DetailPlant({
  open,
  machineId,
  onClose,
}: DetailPlantProps) {
  const [loading, setLoading] = useState(false);
  const [processes, setProcesses] = useState<ManufacturingProcess[]>([]);

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(100);
  const [total, setTotal] = useState(0);

  const fetchManufacturingProcess = useCallback(async () => {
    if (!machineId) {
      setProcesses([]);
      setTotal(0);
      return;
    }

    try {
      setLoading(true);

      const request = {
        machineId,
        page,
        pageSize,
      };

      const result = await getPartProcessByMachineAsync(request);
      const nextProcesses = result.data ?? [];

      setProcesses(nextProcesses);
      setPage(result.page ?? page);
      setPageSize(result.pageSize ?? pageSize);
      setTotal(result.totalItems ?? nextProcesses.length);
    } catch (error) {
      console.error("Lỗi khi lấy danh sách chi tiết cần gia công:", error);
      toast.error("Không thể tải danh sách chi tiết cần gia công.");
    } finally {
      setLoading(false);
    }
  }, [machineId, page, pageSize]);

  useEffect(() => {
    const run = async () => {
      await fetchManufacturingProcess();
    };
    void run();
  }, [fetchManufacturingProcess, machineId]);

  const processingColumns = useMemo<ColumnsType<ManufacturingProcess>>(
    () => [
      {
        title: "#",
        fixed: "left",
        key: "index",
        width: 48,
        align: "center",
        render: (_, __, index) => (page - 1) * pageSize + index + 1,
      },
      {
        title: "Mã Index",
        fixed: "left",
        dataIndex: "indexCode",
        width: 110,
        render: (value) => value || "-",
      },
      {
        title: "Tên chi tiết",
        fixed: "left",
        dataIndex: "partCode",
        width: 170,
        ellipsis: true,
        render: (value) => value || "-",
      },
      {
        title: "Bắt đầu",
        dataIndex: "startTime",
        width: 150,
        render: (value) =>
          value ? dayjs(value).format("DD/MM/YYYY HH:mm:ss") : "-",
      },
      {
        title: "Kết thúc",
        dataIndex: "endTime",
        width: 150,
        render: (
          value: ManufacturingProcess["endTime"],
          process: ManufacturingProcess,
        ) => {
          const displayEndTime = getDisplayProcessEndTime(
            value,
            process.isCutoverCompleted,
          );
          return displayEndTime
            ? displayEndTime.format("DD/MM/YYYY HH:mm:ss")
            : "-";
        },
      },
      {
        title: "Dự kiến (giờ)",
        fixed: "left",
        dataIndex: "estimatedTime",
        width: 170,
        ellipsis: true,
        render: (value) => (value != null ? `${value} giờ` : "-"),
      },
      {
        title: "Máy chạy (giờ)",
        dataIndex: "workingTime",
        width: 120,
        align: "center",
      },
      {
        title: "Gá đặt (giờ)",
        dataIndex: "setupTime",
        width: 120,
        align: "center",
      },
      {
        title: "Chờ (giờ)",
        dataIndex: "waitingTime",
        width: 120,
        align: "center",
      },
      {
        title: "Lập trình (giờ)",
        dataIndex: "programmingTime",
        width: 120,
        align: "center",
      },
      {
        title: "Tổng (giờ)",
        width: 120,
        dataIndex: "totalTime",
        key: "totalTime",
        align: "right",
        render: (value?: number) => value?.toFixed(2) ?? "-",
      },
      {
        title: "Số lượng",
        dataIndex: "quantity",
        width: 90,
        align: "center",
        render: (value) => value ?? 0,
      },
      {
        title: "Đã hoàn thành",
        dataIndex: "processedQuantity",
        width: 150,
        align: "center",
        render: (value) => value ?? 0,
      },
      {
        title: "Ghi chú",
        dataIndex: "note",
        width: 200,
        ellipsis: true,
        render: (value) => value || "-",
      },
      {
        title: "Ghi chú của người vận hành",
        dataIndex: "workerNote",
        width: 200,
        align: "center",
      },
      {
        fixed: "right",
        title: "Trạng thái",
        dataIndex: "status",
        width: 160,
        align: "center",
        render: (status: ProcessStatus) => getStatusTag(status),
      },
    ],
    [page, pageSize],
  );

  const today = new Date().toLocaleDateString("vi-VN", {
    weekday: "long",
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });

  return (
    <>
      <Modal
        title={
          <div className="d-flex ">
            <span>Chi tiết cần gia công</span>
            <span className="text-secondary fs-6 pl-4">{today}</span>
          </div>
        }
        open={open}
        onCancel={onClose}
        width={1200}
        height={600}
        centered
        footer={null}
        destroyOnHidden
      >
        <Table<ManufacturingProcess>
          rowKey="id"
          loading={loading}
          size="small"
          columns={processingColumns}
          dataSource={processes}
          scroll={{
            x: "max-content",
            y: "calc(100vh - 560px)",
          }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            pageSizeOptions: [5, 10, 20, 50, 100],
            showTotal: (value) => `Tổng ${value} chi tiết cần gia công`,
            onChange: (newPage, newPageSize) => {
              const pageSizeChanged = newPageSize !== pageSize;

              setPage(pageSizeChanged ? 1 : newPage);
              setPageSize(newPageSize);
            },
          }}
        />
      </Modal>
    </>
  );
}
