"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Button, Card, DatePicker, Input, Table, Tag } from "antd";
import type { ColumnsType } from "antd/es/table";
import {
  EditOutlined,
  EyeOutlined,
  PlusOutlined,
  SearchOutlined,
} from "@ant-design/icons";
import { toast } from "react-toastify";
import dayjs, { Dayjs } from "dayjs";

import { searchMachineSchedulesAsync } from "@/app/services/machine/MachineService";
import {
  MachineProcess,
  MachineProcessType,
  MachineSchedule,
  PartType,
} from "@/app/types";
import { ProcessStatus } from "@/app/types/common/ProcessStatus";

import "./MachineScheduleBoard.css";
import CreatePart from "../../index/components/CreatePart";
import UpdatePart from "../../index/components/UpdatePart";
import CreateMachineMaintenanceProcess from "./CreateMachineMaintenanceProcess";
import UpdateMachineMaintenanceProcess from "./UpdateMachineMaintenanceProcess";
import { PartTypeLabels } from "@/app/types/part/PartTypeLabels";
import { getStatusText } from "@/app/utilities/UtilityState";

const { RangePicker } = DatePicker;

type DateRange = [Dayjs | null, Dayjs | null] | null;

const getCurrentMonthRange = (): DateRange => [
  dayjs().startOf("month"),
  dayjs().endOf("month"),
];

const formatDate = (date: string) => {
  return dayjs(date).format("DD/MM");
};

const getStatusClassName = (process: MachineProcess, isPastInProgress: boolean) => {
  if (process.isOverdue) {
    if (process.referenceId) {
      return `timeline-card bg-danger-subtle text-danger-emphasis border-danger${
        isPastInProgress ? " opacity-50" : ""
      }`;
    } else {
      return "timeline-card bg-danger-subtle text-danger-emphasis border-danger opacity-50";
    }
  }

  switch (process.status) {
    case ProcessStatus.Completed:
      return "timeline-card timeline-card-done";

    case ProcessStatus.InProgress:
      return isPastInProgress
        ? "timeline-card timeline-card-in-progress timeline-card-in-progress-past"
        : "timeline-card timeline-card-in-progress";

    case ProcessStatus.Cancelled:
      return "timeline-card timeline-card-cancelled";

    case ProcessStatus.Overdue:
      return "timeline-card timeline-card-overdue";

    case ProcessStatus.Planned:
    default:
      return "timeline-card timeline-card-planned";
  }
};

interface Props {
  active: boolean;
}

export function MachineScheduleBoard({ active }: Props) {
  const [isCreatePartOpen, setIsCreatePartOpen] = useState(false);
  const [isUpdatePartOpen, setIsUpdatePartOpen] = useState(false);
  const [selectedPartId, setSelectedPartId] = useState<number | null>(null);
  const [selectedPartReadOnly, setSelectedPartReadOnly] = useState(false);

  const [isCreateMaintenanceOpen, setIsCreateMaintenanceOpen] = useState(false);
  const [isUpdateMaintenanceOpen, setIsUpdateMaintenanceOpen] = useState(false);
  const [selectedMaintenanceId, setSelectedMaintenanceId] = useState<
    number | null
  >(null);

  const handleEditProcess = (process: MachineProcess) => {
    if (process.type === MachineProcessType.Maintenance) {
      setSelectedMaintenanceId(process.id);
      setIsUpdateMaintenanceOpen(true);
      return;
    }

    if (process.isClosed) {
      setSelectedPartId(process.partId);
      setSelectedPartReadOnly(true);
      setIsUpdatePartOpen(true);
      return;
    }

    setSelectedPartId(process.partId);
    setSelectedPartReadOnly(process.isCutoverCompleted);
    setIsUpdatePartOpen(true);
  };

  const [loading, setLoading] = useState(false);
  const [machines, setMachines] = useState<MachineSchedule[]>([]);

  /*
   * Giá trị đang nhập trên giao diện.
   */
  // const [keyword, setKeyword] = useState("");
  const [filterPlanDateRange, setFilterPlanDateRange] =
    useState<DateRange>(getCurrentMonthRange);

  /*
   * Giá trị thực tế dùng để gọi API.
   * Tách riêng để API chỉ chạy khi nhấn "Tìm kiếm".
   */
  const [searchKeyword, setSearchKeyword] = useState("");

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(100);
  const [total, setTotal] = useState(0);

  const fetchMachineSchedule = useCallback(
    async (currentPage = page, currentPageSize = pageSize) => {
      setLoading(true);

      try {
        const response = await searchMachineSchedulesAsync({
          keyword: searchKeyword.trim(),
          plantFrom: filterPlanDateRange?.[0]?.format("YYYY-MM-DD") ?? null,
          plantTo: filterPlanDateRange?.[1]?.format("YYYY-MM-DD") ?? null,
          page: currentPage,
          pageSize: currentPageSize,
        });

        setMachines(response.data ?? []);
        setPage(response.page);
        setPageSize(response.pageSize);
        setTotal(response.totalItems);
      } catch {
        toast.error("Không thể tải danh sách kế hoạch");
      } finally {
        setLoading(false);
      }
    },
    [searchKeyword, filterPlanDateRange, page, pageSize],
  );

  useEffect(() => {
    const run = async () => {
      await fetchMachineSchedule();
    };
    void run();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active]);

  /*
   * Sinh tất cả ngày trong khoảng lọc.
   *
   * Ví dụ:
   * 01/07/2026 - 31/07/2026
   *
   * Timeline sẽ có đủ cột ngày trong khoảng lọc, kể cả ngày không có process.
   */
  const dates = useMemo(() => {
    const fromDate = filterPlanDateRange?.[0];
    const toDate = filterPlanDateRange?.[1];

    if (!fromDate || !toDate) {
      return [];
    }

    const result: string[] = [];
    let currentDate = fromDate.startOf("day");
    const endDate = toDate.startOf("day");

    while (
      currentDate.isBefore(endDate, "day") ||
      currentDate.isSame(endDate, "day")
    ) {
      result.push(currentDate.format("YYYY-MM-DD"));
      currentDate = currentDate.add(1, "day");
    }

    return result;
  }, [filterPlanDateRange]);

  const handleResetFilters = () => {
    const currentMonthRange = getCurrentMonthRange();

    // setKeyword("");
    // setSearchKeyword("");
    setFilterPlanDateRange(currentMonthRange);

    setPage(1);
  };

  const columns: ColumnsType<MachineSchedule> = useMemo(() => {
    const machineColumn: ColumnsType<MachineSchedule>[number] = {
      title: "Máy",
      dataIndex: "name",
      key: "machine",
      fixed: "left",
      width: 250,
      render: (_, machine) => (
        <div className="machine-information">
          <div className="fw-bold">{machine.name}</div>

          <div className="text-muted small mt-1">
            {machine.code}
            {machine.type ? ` - ${machine.type}` : ""}
          </div>

          {machine.brand && (
            <Tag color="blue" className="mt-2">
              {machine.brand}
            </Tag>
          )}
        </div>
      ),
    };

    const timelineColumns: ColumnsType<MachineSchedule> = dates.map((date) => ({
      title: (
        <div className="timeline-date-header">
          <div className="fw-semibold">{formatDate(date)}</div>
          <div className="small text-muted">{dayjs(date).format("ddd")}</div>
        </div>
      ),
      key: date,
      width: 350,
      align: "center",
      render: (_: unknown, machine: MachineSchedule) => {
        const processes = machine.processes.filter((process) => {
          if (!process.plantDate) {
            return false;
          }

          return dayjs(process.plantDate).format("YYYY-MM-DD") === date;
        });

        if (processes.length === 0) {
          return <div className="timeline-empty-box">Không có kế hoạch</div>;
        }

        return (
          <div className="d-flex flex-column gap-2">
            {processes.map((process) => {
              const isMaintenance =
                process.type === MachineProcessType.Maintenance;
              const isPastInProgress =
                process.status === ProcessStatus.InProgress &&
                dayjs(date).isBefore(dayjs().startOf("day"));
              const processCardClass = process.isCutoverCompleted
                ? "timeline-card-cutover-source"
                : process.referenceId != null
                  ? "timeline-card-cutover-continuation"
                  : getStatusClassName(process, isPastInProgress);

              return (
                <div
                  key={`${process.type}-${process.id}`}
                  className={`rounded-3 border p-2 ${
                    isMaintenance
                      ? "border-danger-subtle bg-danger-subtle"
                      : processCardClass
                  }`}
                  style={{
                    minWidth: 220,
                    textAlign: "left",
                  }}
                >
                  {/* Header */}
                  <div className="d-flex align-items-start justify-content-between gap-2">
                    <div className="grow overflow-hidden">
                      <div
                        className={`fw-semibold text-truncate ${
                          isMaintenance ? "text-danger" : ""
                        }`}
                        title={isMaintenance ? "Bảo trì" : process.partCode}
                      >
                        {isMaintenance
                          ? "🔧 Bảo trì"
                          : `(${process.id})📦 ${process.indexCode} - ${process.partCode}`}
                      </div>
                    </div>

                    <div className="d-flex align-items-center gap-1 shrink-0">
                      <Tag
                        color={
                          isMaintenance
                            ? "red"
                            : process.isCutoverCompleted || process.referenceId != null
                              ? "magenta"
                              : undefined
                        }
                        className="m-0"
                      >
                        {process.isCutoverCompleted
                          ? "Đã chốt ngày"
                          : getStatusText(process.status)}
                      </Tag>
                      {!isMaintenance &&
                        (process.isClosed || process.isCutoverCompleted) && (
                        <Tag color="default" className="m-0">
                          Đã đóng
                        </Tag>
                        )}

                      <Button
                        type="text"
                        size="small"
                        icon={
                          !isMaintenance &&
                          (process.isClosed || process.isCutoverCompleted) ? (
                            <EyeOutlined />
                          ) : (
                            <EditOutlined />
                          )
                        }
                        aria-label={
                          isMaintenance
                            ? "Sửa thông tin bảo trì"
                            : process.isClosed
                              ? "Xem part đã đóng"
                              : "Sửa thông tin sản xuất"
                        }
                        title={
                          !isMaintenance &&
                          (process.isClosed || process.isCutoverCompleted)
                            ? "Xem"
                            : "Sửa"
                        }
                        onClick={(event) => {
                          event.stopPropagation();
                          handleEditProcess(process);
                        }}
                      />
                    </div>
                  </div>

                  {/* Nội dung */}
                  <div
                    className="small mt-2 text-body-secondary"
                    style={{
                      lineHeight: 1.5,
                      wordBreak: "break-word",
                    }}
                  >
                    {process.note || "Không có ghi chú"}
                  </div>

                  {/* Thông tin bổ sung */}
                  <div className="d-flex flex-wrap align-items-center column-gap-3 row-gap-1 mt-2">
                    {isMaintenance ? (
                      <>
                        {process.startTime && (
                          <div className="small text-muted">
                            <span className="fw-medium">Bắt đầu:</span>{" "}
                            {process.startTime}
                          </div>
                        )}

                        {process.endTime != null && (
                          <div className="small text-muted">
                            <span className="fw-medium">Kết thúc:</span>{" "}
                            {process.endTime}
                          </div>
                        )}

                        <div className="small text-muted">
                          <span className="fw-medium">Thời gian ước tính:</span>{" "}
                          {process.estimatedTime != null
                            ? `${process.estimatedTime} giờ`
                            : "-"}
                        </div>
                      </>
                    ) : (
                      <div className="small text-muted">
                        {/* Hàng 1 */}
                        <div className="d-flex justify-content-between align-items-center gap-3">
                          <div>
                            <span className="fw-medium">Số lượng: </span>
                            {process.totalProcessedQuantity}/{process.quantity}
                          </div>

                          <div className="text-end">
                            {/* <span className="fw-medium">Loại gia công:</span>{" "} */}
                            {process.partType
                              ? PartTypeLabels[process.partType as PartType]
                              : "-"}
                          </div>
                        </div>

                        {process.referenceId != null && (
                          <div>
                            <span className="fw-medium">
                              Tổng đã làm trước đó:{" "}
                            </span>
                            {process.donePreviouslyQuantity}
                          </div>
                        )}

                        {/* Hàng 2 */}
                        <div className="d-flex justify-content-between align-items-center gap-3 mt-1">
                          <div>
                            <span className="fw-medium">
                              Thời gian ước tính:
                            </span>{" "}
                            {process.estimatedTime != null
                              ? `${process.estimatedTime} giờ`
                              : "-"}
                          </div>

                          <div>
                            {process.prioritize && (
                              <Tag color="orange" className="mb-0">
                                Ưu tiên
                              </Tag>
                            )}
                          </div>
                        </div>

                        {process.isOverdue && (
                          <div className="inline-flex items-center gap-2 rounded-full border border-orange-300 bg-orange-50 px-3 py-1.5 text-sm font-semibold text-orange-700 shadow-sm">
                            {process.referenceId ? (
                              <span>Được gia hạn({process.referenceId})</span>
                            ) : (
                              <span>Quá hạn</span>
                            )}
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        );
      },
    }));

    return [machineColumn, ...timelineColumns];
  }, [dates]);

  return (
    <Card
      className="grow overflow-hidden"
      style={{ minHeight: 0 }}
      styles={{
        body: {
          height: "100%",
          overflow: "hidden",
          padding: 12,
          display: "flex",
          flexDirection: "column",
        },
      }}
    >
      <div className="d-flex justify-content-between gap-3 flex-wrap">
        <div className="d-flex flex-wrap align-items-end gap-3 mb-3">
          <div style={{ width: 240 }}>
            <label className="form-label mb-1">Tên hoặc mã máy</label>

            <Input
              placeholder="Nhập tên hoặc mã máy"
              allowClear
              value={searchKeyword}
              onChange={(event) => setSearchKeyword(event.target.value)}
            />
          </div>

          <div style={{ width: 280 }}>
            <label className="form-label mb-1">Khoảng ngày kế hoạch</label>

            <RangePicker
              value={filterPlanDateRange}
              onChange={(value) => setFilterPlanDateRange(value)}
              style={{ width: "100%" }}
              format="DD/MM/YYYY"
              placeholder={["Từ ngày", "Đến ngày"]}
              allowClear={false}
            />
          </div>

          <Button onClick={handleResetFilters}>Tháng hiện tại</Button>

          <Button
            type="primary"
            icon={<SearchOutlined />}
            onClick={async () => {
              setPage(1);
              await fetchMachineSchedule(1, pageSize);
            }}
          >
            Tìm kiếm
          </Button>
        </div>

        <div className="d-flex align-items-end mb-3 gap-3">
          <Button
            icon={<PlusOutlined />}
            onClick={() => setIsCreateMaintenanceOpen(true)}
          >
            Thêm lịch bảo dưỡng
          </Button>

          <Button
            icon={<PlusOutlined />}
            onClick={() => setIsCreatePartOpen(true)}
          >
            Thêm part
          </Button>
        </div>
      </div>
      <div style={{ flex: 1, minHeight: 0 }}>
        <Table<MachineSchedule>
          rowKey="id"
          loading={loading}
          columns={columns}
          dataSource={machines}
          scroll={{
            x: 220 + dates.length * 190,
            y: "calc(100vh - 320px)",
          }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            pageSizeOptions: [10, 20, 50, 100],
            showTotal: (value) => `Tổng ${value} máy`,
            onChange: (newPage, newPageSize) => {
              /*
               * Khi đổi page size nên quay lại trang 1 để tránh
               * trang hiện tại vượt quá tổng số trang.
               */
              if (newPageSize !== pageSize) {
                setPage(1);
                setPageSize(newPageSize);
                return;
              }

              setPage(newPage);
            },
          }}
          bordered
        />
      </div>

      <CreatePart
        open={isCreatePartOpen}
        status={ProcessStatus.Planned}
        onClose={() => {
          setIsCreatePartOpen(false);
          void fetchMachineSchedule();
        }}
      />

      <UpdatePart
        id={selectedPartId}
        open={isUpdatePartOpen}
        readOnly={selectedPartReadOnly}
        onClose={() => {
          setIsUpdatePartOpen(false);
          setSelectedPartId(null);
          setSelectedPartReadOnly(false);
          void fetchMachineSchedule();
        }}
      />

      <CreateMachineMaintenanceProcess
        open={isCreateMaintenanceOpen}
        onClose={() => {
          setIsCreateMaintenanceOpen(false);
          void fetchMachineSchedule();
        }}
      />

      <UpdateMachineMaintenanceProcess
        id={selectedMaintenanceId}
        open={isUpdateMaintenanceOpen}
        onClose={() => {
          setIsUpdateMaintenanceOpen(false);
          setSelectedMaintenanceId(null);
          void fetchMachineSchedule();
        }}
      />
    </Card>
  );
}
