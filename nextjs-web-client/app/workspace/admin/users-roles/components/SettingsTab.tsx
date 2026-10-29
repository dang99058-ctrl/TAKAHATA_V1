"use client";

import { useCallback, useEffect, useState } from "react";
import {
  Button,
  Card,
  DatePicker,
  Divider,
  Form,
  Input,
  InputNumber,
  Modal,
  Select,
  Space,
  Typography,
} from "antd";
import {
  ApiOutlined,
  GlobalOutlined,
  ReadOutlined,
  SaveOutlined,
  StopOutlined,
  ThunderboltOutlined,
  WifiOutlined,
} from "@ant-design/icons";
import { toast } from "react-toastify";
import dayjs, { type Dayjs } from "dayjs";
import { ApiError } from "@/app/types/common/ApiError";
import {
  clearPendingDailyCutoverAsync,
  getSettingsAsync,
  testConnectionAsync,
  testDailyCutoverAsync,
  testDailyExtensionAsync,
  readMachineBitsAsync,
  updateSettingsAsync,
  writeMachineBitsAsync,
} from "@/app/services/settings/SettingsService";
import { Machine, MachineStatus } from "@/app/types";
import { getMachinesAsync } from "@/app/services/machine/MachineService";

const { Text } = Typography;

interface SettingsFormValues {
  ipAddress: string;
  port: number;
}

type WriteFormValues = {
  status: MachineStatus;
  part: number;
  partProcess: number;
  processStatus: number;
};

type ReadFormValues = {
  status?: MachineStatus;
  part?: number;
  partProcess?: number;
  processStatus?: number;
};

interface TestCutoverFormValues {
  reportDate: Dayjs;
  machineId: number;
}

interface TestExtensionFormValues {
  targetDate: Dayjs;
}

interface Props {
  active: boolean;
}

const DEFAULT_SETTINGS: SettingsFormValues = {
  ipAddress: "192.168.3.250",
  port: 502,
};

const IPV4_PATTERN =
  /^(25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)){3}$/;

export default function SettingsTab({ active }: Props) {
  const [form] = Form.useForm<SettingsFormValues>();
  const [writeForm] = Form.useForm<WriteFormValues>();
  const [readForm] = Form.useForm<ReadFormValues>();

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testingConnection, setTestingConnection] = useState(false);

  const [loadingMachines, setLoadingMachines] = useState(false);
  const [machineSource, setMachineSource] = useState<Machine[]>([]);
  const [testingDailyCutover, setTestingDailyCutover] = useState(false);
  const [testingDailyExtension, setTestingDailyExtension] = useState(false);
  const [clearingPendingCutover, setClearingPendingCutover] = useState(false);
  const [cutoverForm] = Form.useForm<TestCutoverFormValues>();
  const [extensionForm] = Form.useForm<TestExtensionFormValues>();
  const [selectedWriteMachine, setSelectedWriteMachine] =
    useState<Machine | null>(null);
  const [selectedReadMachine, setSelectedReadMachine] =
    useState<Machine | null>(null);
  const [submittingWrite, setSubmittingWrite] = useState(false);
  const [submittingRead, setSubmittingRead] = useState(false);

  const fetchMachineSource = useCallback(async () => {
    setLoadingMachines(true);

    try {
      const data = await getMachinesAsync();
      setMachineSource(data);
    } catch (error) {
      console.error("Lỗi khi lấy danh sách máy:", error);
      toast.error("Không thể tải danh sách máy");
    } finally {
      setLoadingMachines(false);
    }
  }, []);

  const fetchSettings = useCallback(async () => {
    setLoading(true);
    try {
      const data = await getSettingsAsync();

      form.setFieldValue("ipAddress", data.ipAddress);
      form.setFieldValue("port", data.port);
    } catch (error) {
      console.error("Có lỗi xảy ra khi lấy thông tin settings:", error);

      if (error instanceof ApiError) {
        toast.error(error.getDisplayMessage());
        return;
      }

      toast.error("Có lỗi xảy ra khi lấy thông tin settings.");
    } finally {
      setLoading(false);
    }
  }, [form]);

  useEffect(() => {
    const run = async () => {
      Promise.all([fetchMachineSource(), fetchSettings()]);
    };

    void run();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active]);

  const handleSave = async (values: SettingsFormValues) => {
    const request = {
      ipAddress: values.ipAddress,
      port: values.port,
    };

    setSaving(true);

    try {
      await updateSettingsAsync(request);
      toast.success("Lưu cài đặt thành công.");

      const data = await getSettingsAsync();

      console.log(data);
    } catch (error) {
      console.error("Lỗi khi update settings:", error);

      if (error instanceof ApiError) {
        toast.error(error.getDisplayMessage());
        return;
      }

      toast.error("Có lỗi xảy ra khi update settings.");
    } finally {
      setSaving(false);
    }
  };

  const handleTestConnection = async () => {
    setTestingConnection(true);
    try {
      await testConnectionAsync();
      toast.success("Kết nối thành công.");
    } catch (error) {
      console.error("Kết nối thất bại:", error);

      if (error instanceof ApiError) {
        toast.error(error.getDisplayMessage());
        return;
      }

      toast.error("Connect failure.");
    } finally {
      setTestingConnection(false);
    }
  };

  const handleSubmitDailyCutoverTest = (values: TestCutoverFormValues) => {
    const reportDate = values.reportDate.format("YYYY-MM-DD");
    const continuationDate = values.reportDate.add(1, "day").format("DD/MM/YYYY");
    const selectedMachine = machineSource.find(
      (machine) => machine.id === values.machineId,
    );

    Modal.confirm({
      title: "Xác nhận gửi lệnh chốt ngày",
      content: (
        <span>
          Gửi lệnh chốt ngày {values.reportDate.format("DD/MM/YYYY")} cho máy{" "}
          <strong>
            {selectedMachine
              ? `${selectedMachine.name} - ${selectedMachine.code}`
              : values.machineId}
          </strong>
          . Nếu PLC phản hồi thành công, kế hoạch sẽ được chuyển tiếp sang ngày{" "}
          {continuationDate}.
        </span>
      ),
      okText: "Gửi lệnh",
      cancelText: "Hủy",
      okButtonProps: { danger: true },
      onOk: async () => {
        setTestingDailyCutover(true);
        try {
          await testDailyCutoverAsync(reportDate, values.machineId);
          toast.success(
            `Đã gửi lệnh chốt ngày ${values.reportDate.format("DD/MM/YYYY")} cho máy.`,
          );
        } catch (error) {
          console.error("Gửi lệnh kiểm thử chốt ngày thất bại:", error);

          if (error instanceof ApiError) {
            toast.error(error.getDisplayMessage());
          } else {
            toast.error("Gửi lệnh kiểm thử chốt ngày thất bại.");
          }
        } finally {
          setTestingDailyCutover(false);
        }
      },
    });
  };

  const handleSubmitDailyExtensionTest = (
    values: TestExtensionFormValues,
  ) => {
    const targetDate = values.targetDate.format("YYYY-MM-DD");
    const previousDate = values.targetDate.subtract(1, "day").format("DD/MM/YYYY");
    const targetDateLabel = values.targetDate.format("DD/MM/YYYY");

    Modal.confirm({
      title: "Xác nhận chạy thử gia hạn kế hoạch",
      content: (
        <span>
          Chạy logic gia hạn như job 1:00 để chuyển các kế hoạch đủ điều kiện
          từ ngày trước đó ({previousDate}) sang ngày {targetDateLabel} trên{" "}
          <strong>tất cả máy</strong>. Thao tác có thể thay đổi dữ liệu kế
          hoạch.
        </span>
      ),
      okText: "Chạy gia hạn",
      cancelText: "Hủy",
      okButtonProps: { danger: true },
      onOk: async () => {
        setTestingDailyExtension(true);
        try {
          await testDailyExtensionAsync(targetDate);
          toast.success(
            `Đã chạy logic gia hạn kế hoạch sang ngày ${targetDateLabel}.`,
          );
        } catch (error) {
          console.error("Chạy thử gia hạn kế hoạch thất bại:", error);
          if (error instanceof ApiError) {
            toast.error(error.getDisplayMessage());
          } else {
            toast.error("Chạy thử gia hạn kế hoạch thất bại.");
          }
        } finally {
          setTestingDailyExtension(false);
        }
      },
    });
  };

  const handleClearPendingCutover = async () => {
    let machineId: number;
    try {
      const values = await cutoverForm.validateFields(["machineId"]);
      machineId = values.machineId;
    } catch {
      return;
    }

    const selectedMachine = machineSource.find(
      (machine) => machine.id === machineId,
    );

    Modal.confirm({
      title: "Gỡ yêu cầu chốt ngày đang chờ?",
      content: (
        <span>
          Chỉ tiếp tục nếu đã xác nhận PLC sẽ <strong>không còn gửi phản hồi
          status=8 cũ</strong>. Nếu phản hồi cũ đến sau khi trigger lại, hệ thống
          có thể gán nó nhầm cho lần test mới. Máy:{" "}
          <strong>
            {selectedMachine
              ? `${selectedMachine.name} - ${selectedMachine.code}`
              : machineId}
          </strong>
          .
        </span>
      ),
      okText: "Đã xác nhận, gỡ trạng thái chờ",
      cancelText: "Hủy",
      okButtonProps: { danger: true },
      onOk: async () => {
        setClearingPendingCutover(true);
        try {
          const result = await clearPendingDailyCutoverAsync(machineId);
          toast.success(
            `Đã gỡ trạng thái chờ chốt ngày ${dayjs(result.reportDate).format("DD/MM/YYYY")}. Có thể trigger lại.`,
          );
        } catch (error) {
          console.error("Gỡ trạng thái chờ chốt ngày thất bại:", error);
          if (error instanceof ApiError) {
            toast.error(error.getDisplayMessage());
          } else {
            toast.error("Gỡ trạng thái chờ chốt ngày thất bại.");
          }
        } finally {
          setClearingPendingCutover(false);
        }
      },
    });
  };

  const handleWriteMachineChange = (machineId?: number) => {
    const machine = machineSource.find((item) => item.id === machineId) ?? null;

    setSelectedWriteMachine(machine);
    writeForm.resetFields();
  };

  const handleReadMachineChange = (machineId?: number) => {
    const machine = machineSource.find((item) => item.id === machineId) ?? null;

    setSelectedReadMachine(machine);
    readForm.resetFields();
  };

  const handleSubmitWrite = async (values: WriteFormValues) => {
    if (!selectedWriteMachine) {
      toast.warning("Vui lòng chọn máy");
      return;
    }

    if (selectedWriteMachine.statusBitAddress === null) {
      toast.error("Máy chưa cấu hình Status Bit Address");
      return;
    }

    if (selectedWriteMachine.partBitAddress === null) {
      toast.error("Máy chưa cấu hình Part Bit Address");
      return;
    }

    if (selectedWriteMachine.partProcessBitAddress === null) {
      toast.error("Máy chưa cấu hình Part Process Bit Address");
      return;
    }

    const request = {
      statusBitAddress: selectedWriteMachine.statusBitAddress,
      partBitAddress: selectedWriteMachine.partBitAddress,
      partProcessBitAddress: selectedWriteMachine.partProcessBitAddress,
      processStatusBitAddress: selectedWriteMachine.processStatusBitAddress,
      statusValue: values.status,
      partValue: values.part,
      partProcessValue: values.partProcess,
      processStatusValue: values.processStatus,
    };

    try {
      setSubmittingWrite(true);

      await writeMachineBitsAsync(request);

      toast.success("Gửi lệnh ghi thành công");
    } catch (error) {
      console.error(error);
      toast.error("Gửi lệnh ghi thất bại");
    } finally {
      setSubmittingWrite(false);
    }
  };

  const handleSubmitRead = async () => {
    if (!selectedReadMachine) {
      toast.warning("Vui lòng chọn máy");
      return;
    }

    if (selectedReadMachine.statusBitAddress === null) {
      toast.error("Máy chưa cấu hình Status Bit Address");
      return;
    }

    if (selectedReadMachine.partBitAddress === null) {
      toast.error("Máy chưa cấu hình Part Bit Address");
      return;
    }

    if (selectedReadMachine.partProcessBitAddress === null) {
      toast.error("Máy chưa cấu hình Part Process Bit Address");
      return;
    }

    const request = {
      statusBitAddress: selectedReadMachine.statusBitAddress,
      partBitAddress: selectedReadMachine.partBitAddress,
      partProcessBitAddress: selectedReadMachine.partProcessBitAddress,
      processStatusBitAddress: selectedReadMachine.processStatusBitAddress,
    };

    try {
      setSubmittingRead(true);

      const data = await readMachineBitsAsync(request);

      readForm.setFieldsValue({
        status: data.statusValue,
        part: data.partValue,
        partProcess: data.partProcessValue,
        processStatus: data.processStatusValue,
      });

      toast.success("Đọc dữ liệu thành công");
    } catch (error) {
      console.error(error);

      if (error instanceof ApiError) {
        toast.error(error.getDisplayMessage());
        return;
      }

      toast.error("Đọc dữ liệu thất bại");
    } finally {
      setSubmittingRead(false);
    }
  };

  return (
    <div
      className="d-flex flex-column"
      style={{ height: "calc(100vh - 80px)" }}
    >
      <div className="grow overflow-auto pe-2">
        <div className="card-body">
          <Form<SettingsFormValues>
            form={form}
            layout="vertical"
            initialValues={DEFAULT_SETTINGS}
            disabled={loading}
            onFinish={handleSave}
          >
            <Card
              title={
                <Space>
                  <GlobalOutlined />
                  Thông tin PLC
                </Space>
              }
            >
              <div className="row">
                <div className="col-12 col-lg-6">
                  <Form.Item
                    label="Địa chỉ IP"
                    name="ipAddress"
                    rules={[
                      {
                        required: true,
                        message: "Vui lòng nhập địa chỉ IP",
                      },
                      {
                        pattern: IPV4_PATTERN,
                        message: "Địa chỉ IPv4 không hợp lệ",
                      },
                    ]}
                  >
                    <Input
                      prefix={<GlobalOutlined />}
                      placeholder="Ví dụ: 192.168.1.100"
                      maxLength={15}
                    />
                  </Form.Item>
                </div>

                <div className="col-12 col-lg-6">
                  <Form.Item
                    label="Port"
                    name="port"
                    rules={[
                      {
                        required: true,
                        message: "Vui lòng nhập port",
                      },
                      {
                        type: "number",
                        min: 1,
                        max: 65535,
                        message: "Port phải nằm trong khoảng 1 đến 65535",
                      },
                    ]}
                  >
                    <InputNumber
                      className="w-100"
                      min={1}
                      max={65535}
                      precision={0}
                      placeholder="Ví dụ: 5000"
                      prefix={<ApiOutlined />}
                    />
                  </Form.Item>
                </div>
              </div>

              <Divider />
            </Card>

            <div className="d-flex justify-content-end align-items-center gap-2 flex-wrap mt-3">
              <Button
                icon={<WifiOutlined />}
                loading={testingConnection}
                onClick={handleTestConnection}
              >
                Kiểm tra kết nối
              </Button>

              <Button
                type="primary"
                htmlType="submit"
                icon={<SaveOutlined />}
                loading={saving}
              >
                Lưu cài đặt
              </Button>
            </div>
          </Form>

          <Card
            className="mt-4"
            title={
              <Space>
                <ThunderboltOutlined />
                Kiểm thử tách kế hoạch lúc 0:00
              </Space>
            }
          >
            <Typography.Paragraph type="secondary">
              Chọn ngày cần chốt và một máy cụ thể. Lệnh chỉ được gửi đến máy đã
              chọn; ngày tiếp tục là ngày kế tiếp.
            </Typography.Paragraph>
            <Form<TestCutoverFormValues>
              form={cutoverForm}
              layout="vertical"
              onFinish={handleSubmitDailyCutoverTest}
            >
              <div className="row">
                <div className="col-12 col-md-5">
                  <Form.Item
                    label="Ngày cần chốt"
                    name="reportDate"
                    rules={[
                      { required: true, message: "Vui lòng chọn ngày cần chốt" },
                    ]}
                  >
                    <DatePicker
                      className="w-100"
                      format="DD/MM/YYYY"
                      placeholder="Chọn ngày"
                      disabled={testingDailyCutover}
                    />
                  </Form.Item>
                </div>
                <div className="col-12 col-md-5">
                  <Form.Item
                    label="Máy"
                    name="machineId"
                    rules={[{ required: true, message: "Vui lòng chọn máy" }]}
                  >
                    <Select
                      showSearch={{
                        filterOption: (input, option) =>
                          String(option?.label ?? "")
                            .toLocaleLowerCase()
                            .includes(input.trim().toLocaleLowerCase()),
                      }}
                      loading={loadingMachines}
                      placeholder="Chọn máy"
                      disabled={testingDailyCutover}
                      options={machineSource.map((machine) => ({
                        value: machine.id,
                        label: `${machine.name} - ${machine.code}`,
                      }))}
                    />
                  </Form.Item>
                </div>
                <div className="col-12 col-md-2 d-flex align-items-end">
                  <Form.Item className="w-100">
                    <Button
                      type="primary"
                      danger
                      htmlType="submit"
                      icon={<ThunderboltOutlined />}
                      loading={testingDailyCutover}
                      disabled={loadingMachines || clearingPendingCutover}
                      block
                    >
                      Trigger test
                    </Button>
                  </Form.Item>
                </div>
                <div className="col-12">
                  <Button
                    danger
                    icon={<StopOutlined />}
                    loading={clearingPendingCutover}
                    disabled={loadingMachines || testingDailyCutover}
                    onClick={() => void handleClearPendingCutover()}
                  >
                    Gỡ trạng thái chờ PLC (chỉ khi đã xác nhận không còn phản hồi cũ)
                  </Button>
                </div>
              </div>
            </Form>
          </Card>

          <Card
            className="mt-4"
            title={
              <Space>
                <ThunderboltOutlined />
                Kiểm thử gia hạn kế hoạch lúc 1:00
              </Space>
            }
          >
            <Typography.Paragraph type="secondary">
              Chọn ngày muốn gia hạn sang. Chức năng áp dụng logic của job 1:00
              cho tất cả máy; ngày trước đó là ngày nguồn.
            </Typography.Paragraph>
            <Form<TestExtensionFormValues>
              form={extensionForm}
              layout="vertical"
              onFinish={handleSubmitDailyExtensionTest}
            >
              <div className="row">
                <div className="col-12 col-md-8">
                  <Form.Item
                    label="Ngày muốn gia hạn sang"
                    name="targetDate"
                    rules={[
                      {
                        required: true,
                        message: "Vui lòng chọn ngày muốn gia hạn sang",
                      },
                    ]}
                  >
                    <DatePicker
                      className="w-100"
                      format="DD/MM/YYYY"
                      placeholder="Chọn ngày"
                      disabled={testingDailyExtension}
                    />
                  </Form.Item>
                </div>
                <div className="col-12 col-md-4 d-flex align-items-end">
                  <Form.Item className="w-100">
                    <Button
                      type="primary"
                      danger
                      htmlType="submit"
                      icon={<ThunderboltOutlined />}
                      loading={testingDailyExtension}
                      disabled={testingDailyCutover}
                      block
                    >
                      Trigger test
                    </Button>
                  </Form.Item>
                </div>
              </div>
            </Form>
          </Card>

          <div className="mt-4">
            <div className="row g-3">
              <div className="col-12 col-xl-6">
                <Card title="Ghi dữ liệu xuống máy" className="h-100">
                  <Form
                    form={writeForm}
                    layout="vertical"
                    onFinish={handleSubmitWrite}
                    initialValues={{
                      status: MachineStatus.None,
                      part: 0,
                      partProcess: 0,
                      processStatus: 0,
                    }}
                  >
                    <Form.Item label="Máy">
                      <Select
                        allowClear
                        showSearch={{
                          filterOption: (input, option) =>
                            String(option?.code ?? "")
                              .toLocaleLowerCase()
                              .includes(input.trim().toLocaleLowerCase()),
                        }}
                        loading={loadingMachines}
                        placeholder="Chọn máy"
                        value={selectedWriteMachine?.id}
                        options={machineSource.map((machine) => ({
                          value: machine.id,
                          label: `${machine.name} - ${machine.code}`,
                          code: machine.code,
                        }))}
                        onChange={handleWriteMachineChange}
                      />
                    </Form.Item>

                    <div
                      style={{
                        display: "grid",
                        gridTemplateColumns: "minmax(160px, 220px) 1fr",
                        gap: 12,
                        alignItems: "center",
                        marginBottom: 16,
                      }}
                    >
                      <Space orientation="vertical" size={0}>
                        <Text strong>Thanh ghi trạng thái</Text>
                        <Text type="secondary">
                          Địa chỉ:{" "}
                          {selectedWriteMachine?.statusBitAddress ?? "-"}
                        </Text>
                      </Space>

                      <Form.Item
                        name="status"
                        style={{ marginBottom: 0 }}
                        rules={[
                          {
                            required: true,
                            message: "Vui lòng chọn trạng thái",
                          },
                        ]}
                      >
                        <Select
                          placeholder="Chọn trạng thái"
                          disabled={!selectedWriteMachine}
                          options={[
                            { value: MachineStatus.None, label: "None" },
                            { value: MachineStatus.Running, label: "Running" },
                            { value: MachineStatus.Paused, label: "Paused" },
                            { value: MachineStatus.Stopped, label: "Stopped" },
                            { value: MachineStatus.Alarm, label: "Alarm" },
                          ]}
                        />
                      </Form.Item>

                      <Space orientation="vertical" size={0}>
                        <Text strong>Thanh ghi part</Text>
                        <Text type="secondary">
                          Địa chỉ: {selectedWriteMachine?.partBitAddress ?? "-"}
                        </Text>
                      </Space>

                      <Form.Item
                        name="part"
                        style={{ marginBottom: 0 }}
                        rules={[
                          { required: true, message: "Vui lòng nhập Part" },
                        ]}
                      >
                        <InputNumber
                          min={0}
                          precision={0}
                          disabled={!selectedWriteMachine}
                          placeholder="Nhập giá trị Part"
                          style={{ width: "100%" }}
                        />
                      </Form.Item>

                      <Space orientation="vertical" size={0}>
                        <Text strong>Thanh ghi Part Process</Text>
                        <Text type="secondary">
                          Địa chỉ:{" "}
                          {selectedWriteMachine?.partProcessBitAddress ?? "-"}
                        </Text>
                      </Space>

                      <Form.Item
                        name="partProcess"
                        style={{ marginBottom: 0 }}
                        rules={[
                          {
                            required: true,
                            message: "Vui lòng nhập Part Process",
                          },
                        ]}
                      >
                        <InputNumber
                          min={0}
                          precision={0}
                          disabled={!selectedWriteMachine}
                          placeholder="Nhập giá trị Part Process"
                          style={{ width: "100%" }}
                        />
                      </Form.Item>

                      <Space orientation="vertical" size={0}>
                        <Text strong>Thanh ghi trạng thái gửi lệnh</Text>
                        <Text type="secondary">
                          Địa chỉ:{" "}
                          {selectedWriteMachine?.processStatusBitAddress ?? "-"}
                        </Text>
                      </Space>

                      <Form.Item
                        name="processStatus"
                        style={{ marginBottom: 0 }}
                        rules={[
                          {
                            required: true,
                            message: "Vui lòng nhập giá trị",
                          },
                        ]}
                      >
                        <InputNumber
                          min={0}
                          precision={0}
                          disabled={!selectedWriteMachine}
                          placeholder="Nhập giá trị Part Process"
                          style={{ width: "100%" }}
                        />
                      </Form.Item>
                    </div>

                    <Button
                      type="primary"
                      htmlType="submit"
                      loading={submittingWrite}
                      disabled={!selectedWriteMachine}
                      block
                    >
                      Gửi lệnh ghi
                    </Button>
                  </Form>
                </Card>
              </div>

              <div className="col-12 col-xl-6">
                <Card title="Đọc dữ liệu từ máy" className="h-100">
                  <Form form={readForm} layout="vertical">
                    <Form.Item label="Máy">
                      <Select
                        allowClear
                        showSearch={{
                          filterOption: (input, option) =>
                            String(option?.code ?? "")
                              .toLocaleLowerCase()
                              .includes(input.trim().toLocaleLowerCase()),
                        }}
                        loading={loadingMachines}
                        placeholder="Chọn máy"
                        value={selectedReadMachine?.id}
                        options={machineSource.map((machine) => ({
                          value: machine.id,
                          label: `${machine.name} - ${machine.code}`,
                          code: machine.code,
                        }))}
                        onChange={handleReadMachineChange}
                      />
                    </Form.Item>

                    <div
                      style={{
                        display: "grid",
                        gridTemplateColumns: "minmax(160px, 220px) 1fr",
                        gap: 12,
                        alignItems: "center",
                        marginBottom: 16,
                      }}
                    >
                      <Space orientation="vertical" size={0}>
                        <Text strong>Thanh ghi trạng thái</Text>
                        <Text type="secondary">
                          Địa chỉ:{" "}
                          {selectedReadMachine?.statusBitAddress ?? "-"}
                        </Text>
                      </Space>

                      <Form.Item name="status" style={{ marginBottom: 0 }}>
                        <Select
                          disabled
                          placeholder="Giá trị trạng thái"
                          options={[
                            { value: MachineStatus.None, label: "None" },
                            { value: MachineStatus.Running, label: "Running" },
                            { value: MachineStatus.Paused, label: "Paused" },
                            { value: MachineStatus.Stopped, label: "Stopped" },
                            { value: MachineStatus.Alarm, label: "Alarm" },
                          ]}
                        />
                      </Form.Item>

                      <Space orientation="vertical" size={0}>
                        <Text strong>Thanh ghi part</Text>
                        <Text type="secondary">
                          Địa chỉ: {selectedReadMachine?.partBitAddress ?? "-"}
                        </Text>
                      </Space>

                      <Form.Item name="part" style={{ marginBottom: 0 }}>
                        <InputNumber
                          disabled
                          placeholder="Giá trị Part"
                          style={{ width: "100%" }}
                        />
                      </Form.Item>

                      <Space orientation="vertical" size={0}>
                        <Text strong>Thanh ghi Part Process</Text>
                        <Text type="secondary">
                          Địa chỉ:{" "}
                          {selectedReadMachine?.partProcessBitAddress ?? "-"}
                        </Text>
                      </Space>

                      <Form.Item name="partProcess" style={{ marginBottom: 0 }}>
                        <InputNumber
                          disabled
                          placeholder="Giá trị Part Process"
                          style={{ width: "100%" }}
                        />
                      </Form.Item>

                      <Space orientation="vertical" size={0}>
                        <Text strong>Thanh ghi trạng thái gửi lệnh</Text>
                        <Text type="secondary">
                          Địa chỉ:{" "}
                          {selectedReadMachine?.processStatusBitAddress ?? "-"}
                        </Text>
                      </Space>

                      <Form.Item
                        name="processStatus"
                        style={{ marginBottom: 0 }}
                      >
                        <InputNumber
                          disabled
                          placeholder="Giá trị"
                          style={{ width: "100%" }}
                        />
                      </Form.Item>
                    </div>

                    <Button
                      type="default"
                      icon={<ReadOutlined />}
                      loading={submittingRead}
                      disabled={!selectedReadMachine}
                      onClick={() => void handleSubmitRead()}
                      block
                    >
                      Đọc dữ liệu
                    </Button>
                  </Form>
                </Card>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
