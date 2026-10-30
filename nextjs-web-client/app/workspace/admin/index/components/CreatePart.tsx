"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Col,
  DatePicker,
  Flex,
  Form,
  Grid,
  Input,
  InputNumber,
  Modal,
  Row,
  Select,
  Table,
  Tooltip,
  Typography,
} from "antd";
import type { ColumnsType } from "antd/es/table";
import { DeleteOutlined, PlusOutlined, SaveOutlined } from "@ant-design/icons";
import dayjs, { Dayjs } from "dayjs";
import { toast } from "react-toastify";

import { Machine, PartNewRepair, PartType } from "@/app/types";
import { ApiError } from "@/app/types/common/ApiError";
import { ProcessStatus } from "@/app/types/common/ProcessStatus";
import { getMachinesAsync } from "@/app/services/machine/MachineService";
import { createPartAsync } from "@/app/services/index/IndexService";
import { v4 as uuidv4 } from "uuid";

const { Text, Title } = Typography;
const { useBreakpoint } = Grid;

const FORM_ID = "create-part-form";

type Props = {
  open: boolean;
  status: ProcessStatus;
  fixedPlanDate?: Dayjs;
  onClose: () => void;
};

type PlanRow = {
  key: string;
  machineId?: number;
  plantDate?: Dayjs | null;
  status: ProcessStatus;
  note?: string;
  estimatedTime: number | null;
};

type FormValues = {
  IndexCode?: string;
  PartCode: string;
  Quantity: number;
  Prioritize: boolean;
  PartType?: PartType;
  NewRepair?: PartNewRepair | null;
};

const createEmptyRow = (): PlanRow => ({
  key: uuidv4(),
  machineId: undefined,
  plantDate: dayjs(),
  status: ProcessStatus.Planned,
  note: "",
  estimatedTime: null,
});

export default function CreatePart({
  open,
  status,
  fixedPlanDate,
  onClose,
}: Props) {
  const [form] = Form.useForm<FormValues>();
  const screens = useBreakpoint();

  const [loadingMachine, setLoadingMachine] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [machines, setMachines] = useState<Machine[]>([]);
  const [dataSource, setDataSource] = useState<PlanRow[]>([createEmptyRow()]);

  const isDesktop = Boolean(screens.xl);
  const tableScrollY = isDesktop ? "calc(100dvh - 390px)" : 340;

  const fetchMachines = useCallback(async () => {
    setLoadingMachine(true);

    try {
      const data = await getMachinesAsync();
      setMachines(data);
    } catch (error) {
      console.error("Lỗi khi lấy danh sách máy:", error);
      toast.error("Không thể tải danh sách máy.");
    } finally {
      setLoadingMachine(false);
    }
  }, []);

  useEffect(() => {
    const run = async () => {
      await Promise.all([fetchMachines()]);
    };

    if (!open) return;

    void run();
  }, [fetchMachines, open]);

  const machineOptions = useMemo(
    () =>
      machines.map((machine) => ({
        label: `${machine.name} - ${machine.code}`,
        value: machine.id,
        code: machine.code,
      })),
    [machines],
  );

  const resetForm = () => {
    form.resetFields();
    setDataSource([createEmptyRow()]);
  };

  const handleCancel = () => {
    if (submitting) return;

    resetForm();
    onClose();
  };

  const handleChange = <K extends keyof PlanRow>(
    key: string,
    field: K,
    value: PlanRow[K],
  ) => {
    setDataSource((previousRows) =>
      previousRows.map((row) =>
        row.key === key ? { ...row, [field]: value } : row,
      ),
    );
  };

  const handleAddRow = () => {
    setDataSource((previousRows) => [...previousRows, createEmptyRow()]);
  };

  const handleRemoveRow = (key: string) => {
    setDataSource((previousRows) => {
      if (previousRows.length === 1) return previousRows;
      return previousRows.filter((row) => row.key !== key);
    });
  };

  const validatePlanRows = () => {
    const invalidMachineRow = dataSource.findIndex(
      (row) => row.machineId === undefined,
    );
    if (invalidMachineRow !== -1) {
      toast.warning(`Vui lòng chọn máy tại dòng ${invalidMachineRow + 1}.`);
      return false;
    }

    const invalidPlanDateRow = fixedPlanDate
      ? -1
      : dataSource.findIndex((row) => !row.plantDate);
    if (invalidPlanDateRow !== -1) {
      toast.warning(
        `Vui lòng chọn ngày kế hoạch tại dòng ${invalidPlanDateRow + 1}.`,
      );
      return false;
    }

    const invalidEstimatedTime = dataSource.findIndex(
      (row) => !row.estimatedTime || row.estimatedTime <= 0,
    );
    if (invalidEstimatedTime !== -1) {
      toast.warning(
        `Vui lòng nhập thời gian dự kiến tại dòng ${invalidEstimatedTime + 1}.`,
      );
      return false;
    }

    return true;
  };

  const handleSubmit = async (values: FormValues) => {
    if (!validatePlanRows()) return;

    const indexCode = values.IndexCode?.trim();

    const request = {
      indexCode,
      partCode: values.PartCode.trim(),
      quantity: values.Quantity,
      status: status,
      prioritize: values.Prioritize,
      partType: values.PartType,
      newRepair: values.NewRepair ?? null,
      partProcesses: dataSource.map((item, index) => ({
        sequence: index + 1,
        machineId: item.machineId,
        plantDate:
          fixedPlanDate?.format("YYYY-MM-DD") ??
          item.plantDate?.format("YYYY-MM-DD") ??
          null,
        status: item.status,
        note: item.note?.trim() ?? "",
        estimatedTime: item.estimatedTime ?? 0,
      })),
    };

    setSubmitting(true);

    try {
      await createPartAsync(request);
      toast.success("Tạo Part thành công.");
      resetForm();
      onClose();
    } catch (error) {
      console.error("Lỗi khi tạo Part:", error);

      if (error instanceof ApiError) {
        toast.error(error.getDisplayMessage());
        return;
      }

      toast.error("Có lỗi xảy ra khi tạo Part.");
    } finally {
      setSubmitting(false);
    }
  };

  const columns: ColumnsType<PlanRow> = [
    {
      title: "STT",
      key: "sequence",
      fixed: "left",
      width: 64,
      align: "center",
      render: (_, __, index) => index + 1,
    },
    {
      title: "Máy",
      fixed: "left",
      dataIndex: "machineId",
      key: "machineId",
      width: 250,
      render: (value: number | undefined, record) => (
        <Select
          showSearch={{
            filterOption: (input, option) =>
              String(option?.code ?? "")
                .toLocaleLowerCase()
                .includes(input.trim().toLocaleLowerCase()),
          }}
          allowClear
          placeholder="Chọn máy"
          loading={loadingMachine}
          value={value}
          options={machineOptions}
          style={{ width: "100%" }}
          onChange={(machineId: number | undefined) =>
            handleChange(record.key, "machineId", machineId)
          }
        />
      ),
    },
    {
      title: "Ngày kế hoạch",
      dataIndex: "plantDate",
      key: "plantDate",
      width: 170,
      render: (value: Dayjs | null | undefined, record) =>
        fixedPlanDate ? (
          <Text>{fixedPlanDate.format("DD/MM/YYYY")}</Text>
        ) : (
          <DatePicker
            placeholder="Chọn ngày"
            value={value}
            format="DD/MM/YYYY"
            style={{ width: "100%" }}
            onChange={(date) => handleChange(record.key, "plantDate", date)}
          />
        ),
    },
    {
      title: "Thời gian dự kiến (giờ)",
      dataIndex: "estimatedTime",
      key: "estimatedTime",
      width: 170,
      render: (value: number | null | undefined, record) => (
        <InputNumber
          className="w-100"
          min={0}
          onChange={(v) => handleChange(record.key, "estimatedTime", v)}
          value={value}
          step={0.5}
          placeholder="Nhập số giờ làm"
        />
      ),
    },
    {
      title: "Ghi chú",
      dataIndex: "note",
      key: "note",
      width: 280,
      render: (value: string | undefined, record) => (
        <Input
          value={value}
          placeholder="Nhập ghi chú"
          maxLength={500}
          onChange={(event) =>
            handleChange(record.key, "note", event.target.value)
          }
        />
      ),
    },
    {
      title: "",
      key: "action",
      width: 64,
      align: "center",
      fixed: "right",
      render: (_, record) => (
        <Tooltip
          title={
            dataSource.length === 1
              ? "Cần giữ lại ít nhất một công đoạn"
              : "Xóa công đoạn"
          }
        >
          <Button
            danger
            type="text"
            shape="circle"
            icon={<DeleteOutlined />}
            disabled={dataSource.length === 1}
            aria-label="Xóa công đoạn"
            onClick={() => handleRemoveRow(record.key)}
          />
        </Tooltip>
      ),
    },
  ];

  return (
    <Modal
      open={open}
      centered
      width="calc(100vw - 100px)"
      title={
        <Flex vertical gap={2}>
          <Title level={4} style={{ margin: 0 }}>
            Thêm mới Part và nguyên công
          </Title>
          <Text type="secondary">
            Khai báo thông tin Part và kế hoạch thực hiện trên máy
          </Text>
        </Flex>
      }
      onCancel={handleCancel}
      closable={!submitting}
      keyboard={!submitting}
      destroyOnHidden
      confirmLoading={submitting}
      footer={
        <Flex justify="space-between" align="center" wrap gap={12}>
          <Text type="secondary">
            <Text strong>{dataSource.length}</Text> công đoạn đã khai báo
          </Text>

          <Flex gap={8}>
            <Button disabled={submitting} onClick={handleCancel}>
              Đóng
            </Button>
            <Button
              type="primary"
              htmlType="submit"
              form={FORM_ID}
              icon={<SaveOutlined />}
              loading={submitting}
            >
              Lưu Part
            </Button>
          </Flex>
        </Flex>
      }
      styles={{
        container: {
          maxWidth: "calc(100vw - 24px)",
          maxHeight: "calc(100dvh - 24px)",
          display: "flex",
          flexDirection: "column",
          overflow: "hidden",
        },
        header: {
          flexShrink: 0,
          marginBottom: 0,
          paddingBottom: 16,
          borderBottom: "1px solid rgba(5, 5, 5, 0.06)",
        },
        body: {
          flex: 1,
          minHeight: 0,
          maxHeight: "calc(100dvh - 210px)",
          paddingTop: 20,
          paddingBottom: 20,
          overflowY: "auto",
          overflowX: "hidden",
        },
        footer: {
          flexShrink: 0,
          marginTop: 0,
          paddingTop: 16,
          borderTop: "1px solid rgba(5, 5, 5, 0.06)",
        },
      }}
    >
      <Form<FormValues>
        id={FORM_ID}
        form={form}
        layout="vertical"
        requiredMark
        onFinish={handleSubmit}
      >
        <Row gutter={[16, 16]} align="stretch">
          <Col xs={24} xl={6}>
            <Card
              title={
                <Flex vertical gap={2}>
                  <Text strong>Thông tin Part</Text>
                  <Text type="secondary" style={{ fontSize: 12 }}>
                    Nhập thông tin nhận diện của Part
                  </Text>
                </Flex>
              }
              styles={{
                body: { padding: 20 },
              }}
              style={{ height: "100%" }}
            >
              <Form.Item
                label="Index"
                name="IndexCode"
                rules={[
                  { required: true, message: "Vui lòng nhập Index" },
                  { whitespace: true, message: "Index không được để trống" },
                ]}
              >
                <Input
                  allowClear
                  placeholder="Nhập mã Index"
                  maxLength={100}
                  autoFocus
                />
              </Form.Item>

              <Form.Item
                label="Part Name"
                name="PartCode"
                rules={[
                  { required: true, message: "Vui lòng nhập Part name" },
                  {
                    whitespace: true,
                    message: "Part name không được để trống",
                  },
                ]}
              >
                <Input
                  allowClear
                  placeholder="Nhập Part name"
                  maxLength={100}
                />
              </Form.Item>

              <Form.Item
                label="Số lượng"
                name="Quantity"
                rules={[
                  { required: true, message: "Vui lòng nhập số lượng" },
                  { whitespace: true, message: "Số lượng không được để trống" },
                ]}
              >
                <Input
                  allowClear
                  placeholder="Nhập số lượng"
                  maxLength={100}
                  autoFocus
                />
              </Form.Item>

              <Form.Item label="Loại gia công" required>
                <Flex align="center" gap={12} wrap="wrap">
                  <Form.Item
                    name="PartType"
                    rules={[
                      {
                        required: true,
                        message: "Vui lòng chọn loại gia công",
                      },
                    ]}
                    noStyle
                  >
                    <Select
                      placeholder="Chọn loại gia công"
                      allowClear
                      style={{ flex: 1, minWidth: 220 }}
                      options={[
                        { value: PartType.NewMold, label: "NEW MOLD" },
                        {
                          value: PartType.NewMoldRepairMakeNew,
                          label: "NEW MOLD REPAIR",
                        },
                        { value: PartType.MoldEcn, label: "MOLD ECN" },
                        { value: PartType.MoldMp, label: "MOLD MP" },
                        {
                          value: PartType.MoldOvershort,
                          label: "MOLD OVERSHORT",
                        },
                        {
                          value: PartType.JigMoldAssyEng,
                          label: "JIG / MOLD / ASSEMBLY / ENGINEERING",
                        },
                      ]}
                    />
                  </Form.Item>
                  <Form.Item name="NewRepair" noStyle>
                    <Select
                      placeholder="Chọn New / Repair"
                      allowClear
                      style={{ flex: 1, minWidth: 150 }}
                      options={[
                        { value: PartNewRepair.New, label: "New" },
                        { value: PartNewRepair.Repair, label: "Repair" },
                      ]}
                    />
                  </Form.Item>
                </Flex>
              </Form.Item>

              <Form.Item
                name="Prioritize"
                valuePropName="checked"
                initialValue={false}
              >
                <Checkbox>Ưu tiên gia công</Checkbox>
              </Form.Item>

              <Alert
                type="info"
                showIcon
                description="Index chưa tồn tại sẽ được tạo tự động."
              />
            </Card>
          </Col>

          <Col xs={24} xl={18}>
            <Card
              title={
                <Flex vertical gap={2}>
                  <Text strong>Kế hoạch máy</Text>
                  <Text type="secondary" style={{ fontSize: 12 }}>
                    Thiết lập máy, ngày kế hoạch và trạng thái cho từng công
                    đoạn
                  </Text>
                </Flex>
              }
              extra={
                <Button
                  type="primary"
                  ghost
                  icon={<PlusOutlined />}
                  onClick={handleAddRow}
                >
                  Thêm công đoạn
                </Button>
              }
              styles={{ body: { padding: 0 } }}
              style={{ height: "100%", overflow: "hidden" }}
            >
              <Table<PlanRow>
                rowKey="key"
                dataSource={dataSource}
                columns={columns}
                pagination={false}
                size="small"
                bordered
                tableLayout="fixed"
                scroll={{ x: 1020, y: tableScrollY }}
                locale={{ emptyText: "Chưa có công đoạn" }}
              />
            </Card>
          </Col>
        </Row>
      </Form>
    </Modal>
  );
}
