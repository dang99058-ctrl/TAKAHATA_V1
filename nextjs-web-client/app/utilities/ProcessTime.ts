import dayjs, { Dayjs } from "dayjs";

export const getDisplayProcessEndTime = (
  endTime: Dayjs | string | null | undefined,
  isCutoverCompleted: boolean,
): Dayjs | null => {
  if (!endTime) return null;

  const parsedEndTime = dayjs(endTime);
  if (!parsedEndTime.isValid()) return null;

  if (
    isCutoverCompleted &&
    parsedEndTime.hour() === 0 &&
    parsedEndTime.minute() === 0 &&
    parsedEndTime.second() === 0 &&
    parsedEndTime.millisecond() === 0
  ) {
    return parsedEndTime.subtract(1, "second");
  }

  return parsedEndTime;
};
