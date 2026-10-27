import { Dayjs } from "dayjs";
import { ProcessStatus } from "../common/ProcessStatus";
import { ManufacturingProcessType } from "./ManufacturingProcessType";

export interface ManufacturingProcess {
  id: number;
  createAt: string;
  createBy: string;
  updateAt?: string | null;
  updateBy?: string | null;
  indexId: number;
  indexCode: string;
  partId: number;
  partCode: string;
  quantity: number;
  processedQuantity: number;
  totalProcessedQuantity: number;
  referenceId?: number | null;
  status: ProcessStatus;
  note: string;
  startTime?: Dayjs | null;
  endTime?: Dayjs | null;
  estimatedTime?: number | null;
  workingTime?: number | null;
  setupTime?: number | null;
  programmingTime?: number | null;
  waitingTime?: number | null;
  workerNote?: string | null;
  performedBy?: string | null;
  prioritize: boolean | false;
  processType?: ManufacturingProcessType | null;
  isOverdue?: boolean | null;
  isCutoverCompleted: boolean;
  totalTime?: number | null;
  isSettingUp: boolean | false;
}
